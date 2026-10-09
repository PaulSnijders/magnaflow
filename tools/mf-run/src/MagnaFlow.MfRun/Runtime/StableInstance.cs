using System.Globalization;
using MagnaFlow.MfRun.Config;
using MagnaFlow.MfRun.Infrastructure;

namespace MagnaFlow.MfRun.Runtime;

/// <summary>
/// The opt-in stable instance (docs/specs/run/mf-run.md#stable-instance, decision 0018): `promote`
/// publishes into next/, swaps it in as current/ and starts it, keeping the old build in prev/. The
/// process itself is an ordinary service named `stable` (StableConfig.ToService), so start, stop,
/// the PID-reuse guard and status all go through ServiceManager — this class only adds the
/// publish/swap sequence, the lock and state.yml.
/// </summary>
public sealed class StableInstance
{
    public const int Promoted = 0;
    public const int Failed = 1;
    public const int LockHeld = 3;

    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Windows keeps file handles open briefly after a kill, so a rename right after a
    /// stop can fail for a moment — retried for about five seconds.</summary>
    private static readonly TimeSpan RenameRetryDelay = TimeSpan.FromMilliseconds(500);
    private const int RenameAttempts = 10;

    private readonly IProcessSpawner _spawner;
    private readonly IClock _clock;
    private readonly ServiceManager _manager;
    private readonly string _projectRoot;
    private readonly StableConfig _config;
    private readonly Action<string> _log;
    private readonly bool _isWindows;

    public StableInstance(IProcessSpawner spawner, IClock clock, ServiceManager manager, string projectRoot, StableConfig config, Action<string> log, bool isWindows)
    {
        _spawner = spawner;
        _clock = clock;
        _manager = manager;
        _projectRoot = projectRoot;
        _config = config;
        _log = log;
        _isWindows = isWindows;
    }

    public static string NextDirectory(string projectRoot) => Path.Combine(StableConfig.StableDirectory(projectRoot), "next");

    public static string PrevDirectory(string projectRoot) => Path.Combine(StableConfig.StableDirectory(projectRoot), "prev");

    public static string PublishLogPath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "run", "stable-publish.log");

    /// <summary>The stable entry for `status`: ServiceManager's process facts plus state.yml. A
    /// `building` with nobody holding the lock is a promote that was killed mid-way, reported as
    /// failed — status only reads, so state.yml itself keeps saying building until the next promote.</summary>
    public static async Task<ServiceStatusEntry> StatusAsync(ServiceManager manager, string projectRoot, StableConfig config)
    {
        var entry = (await manager.StatusAsync([config.ToService(projectRoot)])).Single();
        var state = StableState.Read(projectRoot);
        if (state?.State == StableState.Building && !PromoteLock.IsHeld(projectRoot))
            state = state with { State = StableState.Failed, Message = "promote interrupted" };

        return entry with
        {
            Stable = true,
            State = state?.State,
            Sha = state?.Sha,
            Dirty = state?.Dirty,
            At = state?.At,
            Message = state?.Message,
            Link = config.Link,
        };
    }

    public async Task<int> PromoteAsync()
    {
        using var promoteLock = PromoteLock.TryAcquire(_projectRoot);
        if (promoteLock is null)
        {
            _log("stable: another promote is running");
            return LockHeld;
        }

        // A failure leaves the old build running, so state.yml keeps describing it: the sha and
        // dirty flag of the last state written before this promote, not the tree that failed.
        var previous = StableState.Read(_projectRoot);
        var (sha, dirty) = await ReadGitAsync();
        WriteState(StableState.Building, sha, dirty, null);
        _log($"stable: promoting {(sha is null ? "(no git HEAD)" : sha[..Math.Min(7, sha.Length)])}{(dirty == true ? " (dirty)" : "")}");

        int Fail(string message)
        {
            WriteState(StableState.Failed, previous?.Sha, previous?.Dirty, message);
            _log($"stable: promote failed: {message}");
            return Failed;
        }

        // --- publish into an emptied next/; the running instance is not touched ---
        var next = NextDirectory(_projectRoot);
        var current = StableConfig.CurrentDirectory(_projectRoot);
        var prev = PrevDirectory(_projectRoot);
        try
        {
            if (Directory.Exists(next))
                Directory.Delete(next, recursive: true);
            Directory.CreateDirectory(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail($"could not empty next/: {ex.Message}");
        }

        var publish = CommandResolver.Resolve(ResolveUnderProjectRoot(_config.Publish), _isWindows);
        if (publish.Path is null)
            return Fail($"publish command not found: tried {string.Join(", ", publish.Attempted)}");

        _log($"stable: publishing with {publish.Path} (limit {_config.Timeout.TotalMinutes:0} min, log {Path.GetRelativePath(_projectRoot, PublishLogPath(_projectRoot))})");
        CommandResult published;
        try
        {
            published = await _spawner.RunAsync(publish.Path, [next], _projectRoot, PublishLogPath(_projectRoot), _config.Timeout);
        }
        catch (Exception ex)
        {
            return Fail($"publish failed to start: {ex.Message}");
        }
        if (published.TimedOut)
            return Fail($"publish timed out after {_config.Timeout.TotalMinutes:0} minute(s)");
        if (published.ExitCode != 0)
            return Fail($"publish exited with code {published.ExitCode}; see .magnaflow/run/stable-publish.log");
        if (CommandResolver.Resolve(Path.Combine(next, _config.Command), _isWindows).Path is null)
            return Fail($"publish output has no {_config.Command}");

        // --- swap: stop, prev/ out, current/ -> prev/, next/ -> current/, start ---
        var service = _config.ToService(_projectRoot);
        var stopped = _manager.Stop([service]).Single();
        if (!stopped.Success)
            return Fail($"could not stop the running instance: {stopped.Message}");

        var hadCurrent = Directory.Exists(current);
        if (hadCurrent)
        {
            var problem = await DeleteWithRetryAsync(prev) ?? await MoveWithRetryAsync(current, prev);
            if (problem is not null)
            {
                await _manager.StartAsync([service]);
                return Fail($"could not move current/ aside ({problem}); previous restarted");
            }
        }

        var swapProblem = await MoveWithRetryAsync(next, current);
        if (swapProblem is not null)
            return await RestoreAsync(service, hadCurrent, $"could not swap in the new build ({swapProblem})");

        var started = (await _manager.StartAsync([service])).Single();
        if (!started.Success)
            return await RestoreAsync(service, hadCurrent, "new build did not start");

        WriteState(StableState.Ready, sha, dirty, null);
        _log("stable: promoted");
        return Promoted;

        // The new build goes back to next/ (inspectable, emptied by the next promote), prev/ back to
        // current/, and the old build is started again.
        async Task<int> RestoreAsync(ServiceConfig stableService, bool previousExists, string reason)
        {
            _manager.Stop([stableService]);
            if (Directory.Exists(current))
            {
                var parked = await DeleteWithRetryAsync(next) ?? await MoveWithRetryAsync(current, next);
                if (parked is not null)
                    await DeleteWithRetryAsync(current);
            }
            if (!previousExists)
                return Fail(reason);

            var restored = await MoveWithRetryAsync(prev, current);
            if (restored is not null)
                return Fail($"{reason}; previous could not be restored ({restored})");
            var restarted = (await _manager.StartAsync([stableService])).Single();
            return Fail(restarted.Success
                ? $"{reason}; previous restored"
                : $"{reason}; previous restored but did not start either");
        }
    }

    private async Task<(string? Sha, bool? Dirty)> ReadGitAsync()
    {
        string? sha = null;
        bool? dirty = null;
        try
        {
            var head = await _spawner.RunAsync("git", ["rev-parse", "HEAD"], _projectRoot, timeout: GitTimeout);
            if (head.Succeeded && head.Output.Trim().Length > 0)
                sha = head.Output.Trim();
            var status = await _spawner.RunAsync("git", ["status", "--porcelain"], _projectRoot, timeout: GitTimeout);
            if (status.Succeeded)
                dirty = status.Output.Trim().Length > 0;
        }
        catch (Exception ex)
        {
            // No git is not a reason to refuse a publish; the row simply shows no sha.
            _log($"stable: git not available ({ex.Message})");
        }
        return (sha, dirty);
    }

    private void WriteState(string state, string? sha, bool? dirty, string? message) =>
        StableState.Write(_projectRoot, new StableState(state, sha, dirty, _clock.UtcNow.ToString("o", CultureInfo.InvariantCulture), message));

    /// <summary>Null on success, else the last error.</summary>
    private async Task<string?> MoveWithRetryAsync(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= RenameAttempts)
                    return ex.Message;
                await _clock.Delay(RenameRetryDelay);
            }
        }
    }

    private async Task<string?> DeleteWithRetryAsync(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= RenameAttempts)
                    return ex.Message;
                await _clock.Delay(RenameRetryDelay);
            }
        }
    }

    private string ResolveUnderProjectRoot(string relativeOrAbsolute) =>
        Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.GetFullPath(Path.Combine(_projectRoot, relativeOrAbsolute));
}
