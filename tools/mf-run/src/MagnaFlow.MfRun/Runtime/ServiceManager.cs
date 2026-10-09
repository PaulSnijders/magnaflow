using System.Globalization;
using System.Runtime.InteropServices;
using MagnaFlow.MfRun.Config;
using MagnaFlow.MfRun.Infrastructure;

namespace MagnaFlow.MfRun.Runtime;

public sealed record ServiceOutcome(string Name, bool Success, string Message);

/// <summary>Reason is set only when Running is false: "no-pid-file", "process-gone", or a
/// "starttime-mismatch: recorded ..., observed ..." message (docs/prompts/0003 "Explain every
/// negative"). PortListening is null when the service has no configured Url to probe. Stable and the
/// fields after it are set only on the stable instance's entry (docs/specs/run/mf-run.md
/// #stable-instance) — additive, and omitted from JSON on every service entry.</summary>
public sealed record ServiceStatusEntry(
    string Name, bool Running, int? Pid, string? Url, string? Reason = null, bool? PortListening = null,
    bool? Stable = null, string? State = null, string? Sha = null, bool? Dirty = null, string? At = null,
    string? Message = null, string? Link = null);

/// <summary>
/// Core start/stop/restart/status mechanics (ontwerp-v0.1.md "Process management"). Every
/// operation is something a human could do by hand — spawn the exe, kill the tree, delete a PID
/// file — this class just does it in the right order with the PID-reuse guard applied.
/// </summary>
public sealed class ServiceManager
{
    private static readonly TimeSpan LivenessWait = TimeSpan.FromSeconds(2);

    /// <summary>The PID-reuse guard exists to catch a *different* process recycling an old PID —
    /// in practice that happens minutes-to-days later, never sub-second. A small tolerance absorbs
    /// timestamp round-tripping (ISO-8601 serialization, OS clock resolution) without weakening the
    /// guard against real reuse (docs/prompts/0003 "Tolerant PID-reuse guard").</summary>
    private static readonly TimeSpan PidReuseTolerance = TimeSpan.FromSeconds(2);

    private const int LogTailLines = 20;

    private readonly IProcessSpawner _spawner;
    private readonly IClock _clock;
    private readonly IPortProbe _portProbe;
    private readonly string _projectRoot;
    private readonly Action<string> _log;

    public ServiceManager(IProcessSpawner spawner, IClock clock, IPortProbe portProbe, string projectRoot, Action<string> log)
    {
        _spawner = spawner;
        _clock = clock;
        _portProbe = portProbe;
        _projectRoot = projectRoot;
        _log = log;
    }

    /// <summary>Starts each service in list order (ontwerp-v0.1.md "start order = list order").</summary>
    public async Task<IReadOnlyList<ServiceOutcome>> StartAsync(IReadOnlyList<ServiceConfig> services)
    {
        var outcomes = new List<ServiceOutcome>();
        foreach (var service in services)
            outcomes.Add(await StartOneAsync(service));
        return outcomes;
    }

    /// <summary>Stops each service in reverse list order (ontwerp-v0.1.md "stop order = reverse").</summary>
    public IReadOnlyList<ServiceOutcome> Stop(IReadOnlyList<ServiceConfig> services)
    {
        var outcomes = new List<ServiceOutcome>();
        for (var i = services.Count - 1; i >= 0; i--)
            outcomes.Add(StopOne(services[i]));
        return outcomes;
    }

    /// <summary>restart = stop + start (ontwerp-v0.1.md "Process management").</summary>
    public async Task<IReadOnlyList<ServiceOutcome>> RestartAsync(IReadOnlyList<ServiceConfig> services)
    {
        var outcomes = new List<ServiceOutcome>(Stop(services));
        outcomes.AddRange(await StartAsync(services));
        return outcomes;
    }

    public async Task<IReadOnlyList<ServiceStatusEntry>> StatusAsync(IReadOnlyList<ServiceConfig> services, CancellationToken cancellationToken = default)
    {
        var results = new List<ServiceStatusEntry>();
        foreach (var service in services)
        {
            var running = TryGetRunningPid(service, out var pid, out var reason);

            bool? portListening = null;
            if (TryGetPort(service.Url, out var port))
                portListening = await _portProbe.IsListeningAsync(port, cancellationToken);

            results.Add(new ServiceStatusEntry(service.Name, running, running ? pid : null, service.Url, running ? null : reason, portListening));
        }
        return results;
    }

    /// <summary>Derives the TCP port to probe from a service's `url` (docs/prompts/0003 "Port
    /// probe as a second, independent signal"). False when there is no url, or it does not parse
    /// as an absolute URI — nothing to probe.</summary>
    private static bool TryGetPort(string? url, out int port)
    {
        port = 0;
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        port = uri.Port;
        return port > 0;
    }

    private async Task<ServiceOutcome> StartOneAsync(ServiceConfig service)
    {
        var pidPath = PidFile.PathFor(_projectRoot, service.Name);

        if (TryGetRunningPid(service, out var existingPid, out var notRunningReason))
        {
            _log($"{service.Name}: already running (pid {existingPid})");
            return new ServiceOutcome(service.Name, true, $"already running (pid {existingPid})");
        }
        CleanStalePidFileIfAny(service, pidPath, notRunningReason);

        var resolution = CommandResolver.Resolve(
            ResolveUnderProjectRoot(service.Command),
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        if (resolution.Path is null)
        {
            var message = resolution.Attempted.Count > 1
                ? $"command not found: tried {string.Join(", ", resolution.Attempted)}"
                : $"command not found: {resolution.Attempted[0]}";
            _log($"{service.Name}: {message}");
            return new ServiceOutcome(service.Name, false, message);
        }
        if (resolution.ResolvedViaFallback)
            _log($"{service.Name}: resolved command to {resolution.Path}");

        var commandPath = resolution.Path;
        var workdir = ResolveUnderProjectRoot(service.Workdir ?? ".");
        var logPath = LogPathFor(service.Name);

        int pid;
        try
        {
            pid = _spawner.Start(commandPath, service.Args, workdir, logPath);
        }
        catch (Exception ex)
        {
            var message = $"failed to start: {ex.Message}";
            _log($"{service.Name}: {message}");
            return new ServiceOutcome(service.Name, false, message);
        }

        var justSpawned = _spawner.GetProcess(pid);
        if (justSpawned is null)
        {
            var message = $"exited immediately{ReadLogTail(logPath)}";
            _log($"{service.Name}: {message}");
            return new ServiceOutcome(service.Name, false, message);
        }
        PidFile.Write(pidPath, pid, justSpawned.StartTimeUtc);

        await _clock.Delay(LivenessWait);

        var afterWait = _spawner.GetProcess(pid);
        if (afterWait is null || afterWait.StartTimeUtc != justSpawned.StartTimeUtc)
        {
            PidFile.Delete(pidPath);
            var message = $"exited shortly after starting{ReadLogTail(logPath)}";
            _log($"{service.Name}: {message}");
            return new ServiceOutcome(service.Name, false, message);
        }

        var startedMessage = $"started (pid {pid})";
        _log($"{service.Name}: {startedMessage}");
        return new ServiceOutcome(service.Name, true, startedMessage);
    }

    private ServiceOutcome StopOne(ServiceConfig service)
    {
        var pidPath = PidFile.PathFor(_projectRoot, service.Name);
        var entry = PidFile.TryRead(pidPath);
        if (entry is null)
        {
            _log($"{service.Name}: not running");
            return new ServiceOutcome(service.Name, true, "not running");
        }

        var reason = EvaluateEntry(entry).Reason;
        if (reason is not null)
        {
            PidFile.Delete(pidPath);
            _log($"{service.Name}: stale pid file removed ({reason})");
            return new ServiceOutcome(service.Name, true, "stale pid file removed");
        }

        try
        {
            _spawner.KillTree(entry.Pid);
        }
        catch (Exception ex)
        {
            var message = $"failed to stop: {ex.Message}";
            _log($"{service.Name}: {message}");
            return new ServiceOutcome(service.Name, false, message);
        }

        PidFile.Delete(pidPath);
        var stoppedMessage = $"stopped (was pid {entry.Pid})";
        _log($"{service.Name}: {stoppedMessage}");
        return new ServiceOutcome(service.Name, true, stoppedMessage);
    }

    /// <summary>True + the PID when a live process still matches the recorded start time within
    /// <see cref="PidReuseTolerance"/> (the PID-reuse guard — ontwerp-v0.1.md "PID files"). False +
    /// a reason ("no-pid-file", "process-gone", or "starttime-mismatch: ...") otherwise. Does not
    /// mutate the PID file either way.</summary>
    private bool TryGetRunningPid(ServiceConfig service, out int pid, out string? reason)
    {
        var entry = PidFile.TryRead(PidFile.PathFor(_projectRoot, service.Name));
        var (running, entryReason) = EvaluateEntry(entry);
        reason = entryReason;
        pid = running ? entry!.Pid : 0;
        return running;
    }

    /// <summary>The PID-reuse guard's actual comparison, shared by every caller that already holds
    /// a <see cref="PidFileEntry"/> (avoids re-reading the PID file for callers that read it for
    /// another reason too, e.g. StopOne needing the PID to kill).</summary>
    private (bool Running, string? Reason) EvaluateEntry(PidFileEntry? entry)
    {
        if (entry is null)
            return (false, "no-pid-file");

        var snapshot = _spawner.GetProcess(entry.Pid);
        if (snapshot is null)
            return (false, "process-gone");

        if ((entry.StartTimeUtc - snapshot.StartTimeUtc).Duration() > PidReuseTolerance)
        {
            var reason = $"starttime-mismatch: recorded {Format(entry.StartTimeUtc)}, observed {Format(snapshot.StartTimeUtc)}";
            return (false, reason);
        }

        return (true, null);
    }

    private static string Format(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);

    private void CleanStalePidFileIfAny(ServiceConfig service, string pidPath, string? reason)
    {
        if (PidFile.TryRead(pidPath) is null)
            return;
        PidFile.Delete(pidPath);
        _log($"{service.Name}: stale pid file removed ({reason})");
    }

    private string LogPathFor(string serviceName) =>
        Path.Combine(_projectRoot, ".magnaflow", "run", $"{serviceName}.log");

    private string ResolveUnderProjectRoot(string relativeOrAbsolute) =>
        Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.GetFullPath(Path.Combine(_projectRoot, relativeOrAbsolute));

    private static string ReadLogTail(string logPath)
    {
        if (!File.Exists(logPath))
            return "";
        var lines = File.ReadAllLines(logPath);
        if (lines.Length == 0)
            return "";
        var tail = lines.Length > LogTailLines ? lines[^LogTailLines..] : lines;
        return "\n  log tail:\n" + string.Join('\n', tail.Select(l => "    " + l));
    }
}
