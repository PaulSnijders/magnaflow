using MagnaFlow.MfRun.Infrastructure;

namespace MagnaFlow.MfRun.Tests;

/// <summary>Disposable temp directory acting as a target project root.</summary>
public sealed class TempProject : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "mf-run-tests", Guid.NewGuid().ToString("N"));

    public TempProject() => Directory.CreateDirectory(Root);

    public string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteConfig(string yaml) => WriteFile(Path.Combine(".magnaflow", "config.yml"), yaml);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);
    public List<TimeSpan> Delays { get; } = [];

    /// <summary>Fires whenever Delay is awaited — tests use this to mutate a FakeProcessSpawner
    /// (e.g. kill a PID) exactly at the point ServiceManager's liveness check would notice it.</summary>
    public Action? OnDelay;

    public Task Delay(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        Delays.Add(duration);
        OnDelay?.Invoke();
        return Task.CompletedTask;
    }
}

/// <summary>Simulates the OS process table instead of spawning anything real. "Alive" PIDs map to
/// the start time ServiceManager should observe; tests mutate that map directly to simulate a
/// process dying, or pre-seed it to simulate a PID recycled by an unrelated process.</summary>
public sealed class FakeProcessSpawner : IProcessSpawner
{
    public sealed record StartCall(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory, string LogPath);

    public List<StartCall> StartCalls { get; } = [];
    public List<int> KillTreeCalls { get; } = [];
    public HashSet<int> KillTreeShouldThrow { get; } = [];

    /// <summary>When true, the next Start() call does not register as alive (simulates a command
    /// that exits before mf-run can even take its first snapshot).</summary>
    public bool NextStartDiesImmediately;

    public DateTimeOffset NextStartTime = new(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<int, DateTimeOffset> _alive = new();
    private int _nextPid = 1000;

    public int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, string logPath)
    {
        var pid = _nextPid++;
        StartCalls.Add(new StartCall(executable, arguments, workingDirectory, logPath));

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, ""); // mirrors the real shell redirect truncating on start

        if (!NextStartDiesImmediately)
            _alive[pid] = NextStartTime;
        NextStartDiesImmediately = false;
        return pid;
    }

    public ProcessSnapshot? GetProcess(int pid) =>
        _alive.TryGetValue(pid, out var startTime) ? new ProcessSnapshot(pid, startTime) : null;

    public void KillTree(int pid)
    {
        KillTreeCalls.Add(pid);
        if (KillTreeShouldThrow.Contains(pid))
            throw new InvalidOperationException("simulated kill failure");
        _alive.Remove(pid);
    }

    /// <summary>Test hook: simulate the process dying on its own (crash, or killed outside mf-run).</summary>
    public void Kill(int pid) => _alive.Remove(pid);

    /// <summary>Test hook: simulate a PID file pointing at a process that is running, but is not
    /// the one mf-run started (a different process now recycles that PID).</summary>
    public void SeedAlive(int pid, DateTimeOffset startTime) => _alive[pid] = startTime;
}

/// <summary>Simulates the port-probe seam so tests can assert PortListening without opening real
/// sockets. Defaults every port to not-listening; tests seed the ports they want reported as up.</summary>
public sealed class FakePortProbe : IPortProbe
{
    private readonly HashSet<int> _listening = [];

    public FakePortProbe SeedListening(int port)
    {
        _listening.Add(port);
        return this;
    }

    public Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken = default) =>
        Task.FromResult(_listening.Contains(port));
}
