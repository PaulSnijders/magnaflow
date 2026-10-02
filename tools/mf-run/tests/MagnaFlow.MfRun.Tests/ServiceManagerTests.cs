using System.Runtime.InteropServices;
using MagnaFlow.MfRun.Config;
using MagnaFlow.MfRun.Runtime;

namespace MagnaFlow.MfRun.Tests;

public class ServiceManagerTests
{
    private static ServiceConfig Service(string name, string commandPath, string? url = null) =>
        new(name, commandPath, [], null, url);

    private static ServiceManager Manager(TempProject project, FakeProcessSpawner spawner, FakeClock clock, out List<string> logLines) =>
        Manager(project, spawner, clock, new FakePortProbe(), out logLines);

    private static ServiceManager Manager(TempProject project, FakeProcessSpawner spawner, FakeClock clock, FakePortProbe portProbe, out List<string> logLines)
    {
        var lines = new List<string>();
        logLines = lines;
        return new ServiceManager(spawner, clock, portProbe, project.Root, lines.Add);
    }

    private static string PidPath(TempProject project, string serviceName) =>
        PidFile.PathFor(project.Root, serviceName);

    [Fact]
    public async Task StartSpawnsAndWritesPidFile()
    {
        using var project = new TempProject();
        var commandPath = project.WriteFile("web.exe", "stub");
        var spawner = new FakeProcessSpawner();
        var clock = new FakeClock();
        var manager = Manager(project, spawner, clock, out _);

        var outcomes = await manager.StartAsync([Service("web", commandPath)]);

        Assert.True(outcomes.Single().Success);
        var pidEntry = PidFile.TryRead(PidPath(project, "web"));
        Assert.NotNull(pidEntry);
        var call = Assert.Single(spawner.StartCalls);
        Assert.Equal(commandPath, call.Executable);
        var snapshot = spawner.GetProcess(pidEntry!.Pid);
        Assert.NotNull(snapshot);
        Assert.Equal(pidEntry.StartTimeUtc, snapshot!.StartTimeUtc);
    }

    [Fact]
    public async Task StartOrderMatchesConfigListOrder()
    {
        using var project = new TempProject();
        var api = project.WriteFile("api.exe", "stub");
        var web = project.WriteFile("web.exe", "stub");
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);

        await manager.StartAsync([Service("api", api), Service("web", web)]);

        Assert.Equal([api, web], spawner.StartCalls.Select(c => c.Executable));
    }

    [Fact]
    public void StopOrderIsReverseOfConfigListOrder()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);

        var api = SeedRunning(project, spawner, "api");
        var web = SeedRunning(project, spawner, "web");

        manager.Stop([Service("api", "api.exe"), Service("web", "web.exe")]);

        Assert.Equal([web, api], spawner.KillTreeCalls);
    }

    [Fact]
    public async Task StartIsNoOpWhenAlreadyRunning()
    {
        using var project = new TempProject();
        var commandPath = project.WriteFile("web.exe", "stub");
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out var log);

        SeedRunning(project, spawner, "web");

        var outcomes = await manager.StartAsync([Service("web", commandPath)]);

        Assert.True(outcomes.Single().Success);
        Assert.Single(spawner.StartCalls); // only SeedRunning's own spawn — no new one from Start()
        Assert.Contains(log, l => l.Contains("already running"));
    }

    [Fact]
    public async Task StalePidFileIsClearedBeforeStarting()
    {
        using var project = new TempProject();
        var commandPath = project.WriteFile("web.exe", "stub");
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out var log);

        // pid file exists, but points at a pid that's either not running or now a different
        // process (recorded start time won't match whatever the spawner reports for that pid)
        PidFile.Write(PidPath(project, "web"), 99999, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var outcomes = await manager.StartAsync([Service("web", commandPath)]);

        Assert.True(outcomes.Single().Success);
        Assert.Single(spawner.StartCalls); // a fresh process was actually spawned
        Assert.Contains(log, l => l.Contains("stale pid file removed"));
    }

    [Fact]
    public async Task StartRefusesWhenCommandFileMissing()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var missingPath = Path.Combine(project.Root, "does-not-exist.exe");

        var outcomes = await manager.StartAsync([Service("web", missingPath)]);

        var outcome = outcomes.Single();
        Assert.False(outcome.Success);
        Assert.Contains("command not found", outcome.Message);
        Assert.Empty(spawner.StartCalls);
        Assert.Null(PidFile.TryRead(PidPath(project, "web")));
    }

    [Fact]
    public async Task StartResolvesExtensionlessCommandToTheCurrentOsWrapper()
    {
        using var project = new TempProject();
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var wrapperPath = project.WriteFile(isWindows ? "web.cmd" : "web.sh", "stub");
        var extensionless = Path.Combine(project.Root, "web");
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out var log);

        var outcomes = await manager.StartAsync([Service("web", extensionless)]);

        Assert.True(outcomes.Single().Success);
        Assert.Equal(wrapperPath, Assert.Single(spawner.StartCalls).Executable);
        Assert.Contains(log, l => l.Contains($"resolved command to {wrapperPath}"));
    }

    [Fact]
    public async Task StartDoesNotFallBackWhenCommandHasAnExtension()
    {
        using var project = new TempProject();
        project.WriteFile("web.exe", "stub"); // exists, but the service asks for web.cmd specifically
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var missingCmd = Path.Combine(project.Root, "web.cmd");

        var outcomes = await manager.StartAsync([Service("web", missingCmd)]);

        var outcome = outcomes.Single();
        Assert.False(outcome.Success);
        Assert.Equal($"command not found: {missingCmd}", outcome.Message);
        Assert.Empty(spawner.StartCalls);
    }

    [Fact]
    public async Task StartNotFoundMessageListsEveryExtensionlessCandidateTried()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var missing = Path.Combine(project.Root, "web"); // no literal file, no OS wrapper either

        var outcomes = await manager.StartAsync([Service("web", missing)]);

        var outcome = outcomes.Single();
        Assert.False(outcome.Success);
        Assert.StartsWith("command not found: tried ", outcome.Message);
        Assert.Contains(missing, outcome.Message);
        Assert.Empty(spawner.StartCalls);
    }

    [Fact]
    public async Task StartReportsFailureWhenProcessDiesImmediately()
    {
        using var project = new TempProject();
        var commandPath = project.WriteFile("web.exe", "stub");
        var spawner = new FakeProcessSpawner { NextStartDiesImmediately = true };
        var manager = Manager(project, spawner, new FakeClock(), out _);

        var outcomes = await manager.StartAsync([Service("web", commandPath)]);

        var outcome = outcomes.Single();
        Assert.False(outcome.Success);
        Assert.Null(PidFile.TryRead(PidPath(project, "web")));
    }

    [Fact]
    public async Task StartReportsFailureWithLogTailWhenProcessDiesDuringLivenessWait()
    {
        using var project = new TempProject();
        var commandPath = project.WriteFile("web.exe", "stub");
        var spawner = new FakeProcessSpawner();
        var clock = new FakeClock();
        var manager = Manager(project, spawner, clock, out _);

        var logPath = Path.Combine(project.Root, ".magnaflow", "run", "web.log");
        clock.OnDelay = () =>
        {
            // simulate: the process wrote a fatal error, then exited, before the 2s check
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, "starting up\nFATAL: port already in use\n");
            var pid = spawner.StartCalls.Count > 0 ? PidFile.TryRead(PidPath(project, "web"))!.Pid : 0;
            spawner.Kill(pid);
        };

        var outcomes = await manager.StartAsync([Service("web", commandPath)]);

        var outcome = outcomes.Single();
        Assert.False(outcome.Success);
        Assert.Contains("FATAL: port already in use", outcome.Message);
        Assert.Null(PidFile.TryRead(PidPath(project, "web")));
        Assert.Single(clock.Delays);
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Delays[0]);
    }

    [Fact]
    public void StopIsIdempotentWhenNotRunning()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out var log);

        var outcomes = manager.Stop([Service("web", "web.exe")]);

        Assert.True(outcomes.Single().Success);
        Assert.Empty(spawner.KillTreeCalls);
        Assert.Contains(log, l => l.Contains("not running"));
    }

    [Fact]
    public void StopKillsTreeAndDeletesPidFile()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var pid = SeedRunning(project, spawner, "web");

        var outcomes = manager.Stop([Service("web", "web.exe")]);

        Assert.True(outcomes.Single().Success);
        Assert.Equal([pid], spawner.KillTreeCalls);
        Assert.Null(PidFile.TryRead(PidPath(project, "web")));
    }

    [Fact]
    public void StopNeverKillsARecycledPid()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out var log);

        // pid file says 5000 started at T1; the OS now reports pid 5000 alive but started at T2 —
        // a different, unrelated process recycling the same pid.
        PidFile.Write(PidPath(project, "web"), 5000, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        spawner.SeedAlive(5000, new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero));

        var outcomes = manager.Stop([Service("web", "web.exe")]);

        Assert.True(outcomes.Single().Success);
        Assert.Empty(spawner.KillTreeCalls);
        Assert.Null(PidFile.TryRead(PidPath(project, "web")));
        Assert.Contains(log, l => l.Contains("stale pid file removed"));
    }

    [Fact]
    public void StopReportsFailureWhenKillThrowsAndKeepsPidFile()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var pid = SeedRunning(project, spawner, "web");
        spawner.KillTreeShouldThrow.Add(pid);

        var outcomes = manager.Stop([Service("web", "web.exe")]);

        Assert.False(outcomes.Single().Success);
        Assert.NotNull(PidFile.TryRead(PidPath(project, "web")));
    }

    [Fact]
    public async Task RestartStopsThenStartsInCorrectOrders()
    {
        using var project = new TempProject();
        var api = project.WriteFile("api.exe", "stub");
        var web = project.WriteFile("web.exe", "stub");
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);

        SeedRunning(project, spawner, "api");
        SeedRunning(project, spawner, "web");

        await manager.RestartAsync([Service("api", api), Service("web", web)]);

        Assert.Equal(2, spawner.KillTreeCalls.Count); // stop phase: web, api
        // start phase (api, web); the first two StartCalls are SeedRunning's setup spawns
        Assert.Equal([api, web], spawner.StartCalls.Skip(2).Select(c => c.Executable));
    }

    [Fact]
    public async Task StatusReportsRunningAndStoppedWithUrl()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var pid = SeedRunning(project, spawner, "web");

        var statuses = await manager.StatusAsync([
            Service("web", "web.exe", url: "http://worker-1:5000"),
            Service("api", "api.exe"),
        ]);

        var web = statuses.Single(s => s.Name == "web");
        Assert.True(web.Running);
        Assert.Equal(pid, web.Pid);
        Assert.Equal("http://worker-1:5000", web.Url);
        Assert.Null(web.Reason);

        var api = statuses.Single(s => s.Name == "api");
        Assert.False(api.Running);
        Assert.Null(api.Pid);
        Assert.Equal("no-pid-file", api.Reason);
    }

    [Fact]
    public async Task StatusReportsProcessGoneWhenPidFileOutlivesTheProcess()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var pid = SeedRunning(project, spawner, "web");
        spawner.Kill(pid); // crashed, or killed outside mf-run — pid file survives

        var statuses = await manager.StatusAsync([Service("web", "web.exe")]);

        var web = Assert.Single(statuses);
        Assert.False(web.Running);
        Assert.Equal("process-gone", web.Reason);
    }

    [Theory]
    [InlineData(0)] // exact tick match — always ran
    [InlineData(500)]
    [InlineData(2000)] // exactly at the tolerance boundary — still within it
    public async Task StatusToleratesSubToleranceStartTimeDrift(int driftMilliseconds)
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var recorded = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);
        var observed = recorded + TimeSpan.FromMilliseconds(driftMilliseconds);
        PidFile.Write(PidFile.PathFor(project.Root, "web"), 5000, recorded);
        spawner.SeedAlive(5000, observed);

        var statuses = await manager.StatusAsync([Service("web", "web.exe")]);

        var web = Assert.Single(statuses);
        Assert.True(web.Running);
        Assert.Equal(5000, web.Pid);
        Assert.Null(web.Reason);
    }

    [Fact]
    public async Task StatusReportsStarttimeMismatchWithBothTimestampsBeyondTolerance()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        var recorded = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);
        var observed = recorded + TimeSpan.FromMilliseconds(2001); // 1ms past the tolerance
        PidFile.Write(PidFile.PathFor(project.Root, "web"), 5000, recorded);
        spawner.SeedAlive(5000, observed);

        var statuses = await manager.StatusAsync([Service("web", "web.exe")]);

        var web = Assert.Single(statuses);
        Assert.False(web.Running);
        Assert.Null(web.Pid);
        Assert.StartsWith("starttime-mismatch:", web.Reason);
        Assert.Contains(recorded.ToString("o"), web.Reason);
        Assert.Contains(observed.ToString("o"), web.Reason);
    }

    [Fact]
    public async Task StatusReportsPortListeningIndependentlyOfTrackedProcess()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var portProbe = new FakePortProbe().SeedListening(5000);
        var manager = Manager(project, spawner, new FakeClock(), portProbe, out _);
        // not tracked by mf-run at all (no pid file) — but something answers the configured port
        var statuses = await manager.StatusAsync([Service("web", "web.exe", url: "http://localhost:5000")]);

        var web = Assert.Single(statuses);
        Assert.False(web.Running);
        Assert.Equal("no-pid-file", web.Reason);
        Assert.True(web.PortListening);
    }

    [Fact]
    public async Task StatusReportsPortNotListeningWhenNothingAnswers()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), new FakePortProbe(), out _);
        var pid = SeedRunning(project, spawner, "web");

        var statuses = await manager.StatusAsync([Service("web", "web.exe", url: "http://localhost:5000")]);

        var web = Assert.Single(statuses);
        Assert.True(web.Running);
        Assert.Equal(pid, web.Pid);
        Assert.False(web.PortListening);
    }

    [Fact]
    public async Task StatusLeavesPortListeningNullWhenServiceHasNoUrl()
    {
        using var project = new TempProject();
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out _);
        SeedRunning(project, spawner, "web");

        var statuses = await manager.StatusAsync([Service("web", "web.exe")]);

        Assert.Null(Assert.Single(statuses).PortListening);
    }

    [Fact]
    public async Task StartLogsTheReasonWhenTreatingAPidFileAsStale()
    {
        using var project = new TempProject();
        var commandPath = project.WriteFile("web.exe", "stub");
        var spawner = new FakeProcessSpawner();
        var manager = Manager(project, spawner, new FakeClock(), out var log);

        // pid file points at a pid the spawner has never heard of — process-gone, not no-pid-file
        PidFile.Write(PidFile.PathFor(project.Root, "web"), 99999, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await manager.StartAsync([Service("web", commandPath)]);

        Assert.Contains(log, l => l.Contains("stale pid file removed (process-gone)"));
    }

    /// <summary>Spawns a fake process and records a matching PID file, as if a prior `start` had
    /// succeeded — returns the PID.</summary>
    private static int SeedRunning(TempProject project, FakeProcessSpawner spawner, string serviceName)
    {
        var pid = spawner.Start($"{serviceName}.exe", [], project.Root, Path.Combine(project.Root, ".magnaflow", "run", $"{serviceName}.log"));
        var snapshot = spawner.GetProcess(pid)!;
        PidFile.Write(PidFile.PathFor(project.Root, serviceName), pid, snapshot.StartTimeUtc);
        return pid;
    }
}
