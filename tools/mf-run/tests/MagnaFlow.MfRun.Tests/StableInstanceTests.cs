using System.Text.Json;
using MagnaFlow.MfRun.Config;
using MagnaFlow.MfRun.Infrastructure;
using MagnaFlow.MfRun.Runtime;

namespace MagnaFlow.MfRun.Tests;

public class StableInstanceTests : IDisposable
{
    private const string NewSha = "1111111111111111111111111111111111111111";
    private const string OldSha = "0000000000000000000000000000000000000000";

    private readonly TempProject _project = new();
    private readonly FakeProcessSpawner _spawner = new();
    private readonly FakeClock _clock = new();
    private readonly StableConfig _config = new("publish.sh", "App.exe", [], null, "https://host/app/", TimeSpan.FromMinutes(15));

    /// <summary>What the publish step does; default: write the new build into next/ and exit 0.</summary>
    private Func<FakeProcessSpawner.RunCall, CommandResult> _publish;

    public StableInstanceTests()
    {
        _project.WriteFile("publish.sh", "stub");
        _publish = call =>
        {
            File.WriteAllText(Path.Combine(call.Arguments[0], "App.exe"), "new");
            return new CommandResult(0, "", false);
        };
        _spawner.OnRun = call => call switch
        {
            { Executable: "git", Arguments: ["rev-parse", "HEAD"] } => new CommandResult(0, NewSha + "\n", false),
            { Executable: "git", Arguments: ["status", "--porcelain"] } => new CommandResult(0, " M file.cs\n", false),
            _ => _publish(call),
        };
    }

    public void Dispose() => _project.Dispose();

    private string Current => StableConfig.CurrentDirectory(_project.Root);
    private string Prev => StableInstance.PrevDirectory(_project.Root);
    private string Next => StableInstance.NextDirectory(_project.Root);

    private ServiceManager Manager() => new(_spawner, _clock, new FakePortProbe(), _project.Root, _ => { });

    private StableInstance Instance() => new(_spawner, _clock, Manager(), _project.Root, _config, _ => { }, isWindows: false);

    /// <summary>An earlier promote: current/ holds the old build, its process runs, state.yml says ready.</summary>
    private int SeedRunningOldBuild()
    {
        _project.WriteFile(".magnaflow/stable/current/App.exe", "old");
        var pid = _spawner.Start(Path.Combine(Current, "App.exe"), [], Current, Path.Combine(_project.Root, ".magnaflow", "run", "stable.log"));
        PidFile.Write(PidFile.PathFor(_project.Root, "stable"), pid, _spawner.GetProcess(pid)!.StartTimeUtc);
        _spawner.StartCalls.Clear();
        StableState.Write(_project.Root, new StableState("ready", OldSha, false, "2026-10-01T00:00:00Z", null));
        return pid;
    }

    private string CurrentBuild => File.ReadAllText(Path.Combine(Current, "App.exe"));

    [Fact]
    public async Task PromoteSwapsInTheNewBuildAndKeepsPrev()
    {
        var oldPid = SeedRunningOldBuild();

        var exit = await Instance().PromoteAsync();

        Assert.Equal(StableInstance.Promoted, exit);
        Assert.Equal("new", CurrentBuild);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Prev, "App.exe")));
        Assert.False(Directory.Exists(Next));
        Assert.Contains(oldPid, _spawner.KillTreeCalls);
        var start = Assert.Single(_spawner.StartCalls);
        Assert.Equal(Path.Combine(Current, "App.exe"), start.Executable);
        Assert.Equal(Current, start.WorkingDirectory);
        var state = StableState.Read(_project.Root)!;
        Assert.Equal("ready", state.State);
        Assert.Equal(NewSha, state.Sha);
        Assert.True(state.Dirty);
        Assert.Null(state.Message);
    }

    [Fact]
    public async Task PromoteWritesBuildingWhilePublishing()
    {
        string? stateDuringPublish = null;
        _publish = call =>
        {
            stateDuringPublish = StableState.Read(_project.Root)?.State;
            File.WriteAllText(Path.Combine(call.Arguments[0], "App.exe"), "new");
            return new CommandResult(0, "", false);
        };

        await Instance().PromoteAsync();

        Assert.Equal("building", stateDuringPublish);
    }

    [Fact]
    public async Task FirstPromoteHasNoPrev()
    {
        var exit = await Instance().PromoteAsync();

        Assert.Equal(StableInstance.Promoted, exit);
        Assert.Equal("new", CurrentBuild);
        Assert.False(Directory.Exists(Prev));
    }

    [Fact]
    public async Task PublishRunsInProjectRootWithAbsoluteNextAndLogsToPublishLog()
    {
        await Instance().PromoteAsync();

        var publish = _spawner.RunCalls.Single(c => c.Executable != "git");
        Assert.Equal(Path.Combine(_project.Root, "publish.sh"), publish.Executable);
        Assert.Equal([Next], publish.Arguments);
        Assert.Equal(_project.Root, publish.WorkingDirectory);
        Assert.Equal(StableInstance.PublishLogPath(_project.Root), publish.LogPath);
        Assert.Equal(TimeSpan.FromMinutes(15), publish.Timeout);
    }

    [Fact]
    public async Task FailedPublishLeavesTheRunningInstanceAlone()
    {
        SeedRunningOldBuild();
        _publish = _ => new CommandResult(2, "", false);

        var exit = await Instance().PromoteAsync();

        Assert.Equal(StableInstance.Failed, exit);
        Assert.Equal("old", CurrentBuild);
        Assert.Empty(_spawner.KillTreeCalls);
        Assert.Empty(_spawner.StartCalls);
        var state = StableState.Read(_project.Root)!;
        Assert.Equal("failed", state.State);
        Assert.Contains("exited with code 2", state.Message);
        Assert.Equal(OldSha, state.Sha); // still describes what runs
    }

    [Fact]
    public async Task PublishTimeoutFails()
    {
        SeedRunningOldBuild();
        _publish = _ => new CommandResult(-1, "", TimedOut: true);

        var exit = await Instance().PromoteAsync();

        Assert.Equal(StableInstance.Failed, exit);
        Assert.Equal("old", CurrentBuild);
        Assert.Empty(_spawner.KillTreeCalls);
        var state = StableState.Read(_project.Root)!;
        Assert.Equal("failed", state.State);
        Assert.Contains("timed out after 15 minute(s)", state.Message);
    }

    [Fact]
    public async Task PublishWithoutCommandInOutputFails()
    {
        SeedRunningOldBuild();
        _publish = _ => new CommandResult(0, "", false);

        var exit = await Instance().PromoteAsync();

        Assert.Equal(StableInstance.Failed, exit);
        Assert.Contains("has no App.exe", StableState.Read(_project.Root)!.Message);
        Assert.Empty(_spawner.KillTreeCalls);
    }

    [Fact]
    public async Task NewProcessThatDiesRestoresAndStartsPrevious()
    {
        SeedRunningOldBuild();
        _spawner.NextStartDiesImmediately = true;

        var exit = await Instance().PromoteAsync();

        Assert.Equal(StableInstance.Failed, exit);
        Assert.Equal("old", CurrentBuild);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Next, "App.exe")));
        Assert.Equal(2, _spawner.StartCalls.Count);
        var entry = PidFile.TryRead(PidFile.PathFor(_project.Root, "stable"));
        Assert.NotNull(_spawner.GetProcess(entry!.Pid));
        var state = StableState.Read(_project.Root)!;
        Assert.Equal("failed", state.State);
        Assert.Equal("new build did not start; previous restored", state.Message);
        Assert.Equal(OldSha, state.Sha);
    }

    [Fact]
    public async Task HeldLockExitsThreeAndChangesNothing()
    {
        var oldPid = SeedRunningOldBuild();
        var before = File.ReadAllText(StableState.PathFor(_project.Root));
        using var held = PromoteLock.TryAcquire(_project.Root);
        Assert.NotNull(held);

        var exit = await Instance().PromoteAsync();

        Assert.Equal(StableInstance.LockHeld, exit);
        Assert.Equal(before, File.ReadAllText(StableState.PathFor(_project.Root)));
        Assert.Empty(_spawner.RunCalls);
        Assert.Empty(_spawner.KillTreeCalls);
        Assert.NotNull(_spawner.GetProcess(oldPid));
        Assert.Equal("old", CurrentBuild);
    }

    [Fact]
    public async Task LockIsFreedAfterPromote()
    {
        await Instance().PromoteAsync();

        Assert.False(PromoteLock.IsHeld(_project.Root));
    }

    [Fact]
    public async Task StaleBuildingWithoutLockReadsAsFailed()
    {
        StableState.Write(_project.Root, new StableState("building", NewSha, false, "2026-10-01T00:00:00Z", null));

        var entry = await StableInstance.StatusAsync(Manager(), _project.Root, _config);

        Assert.Equal("failed", entry.State);
        Assert.Equal("promote interrupted", entry.Message);
        Assert.Equal("building", StableState.Read(_project.Root)!.State); // status only reads
    }

    [Fact]
    public async Task BuildingWithLockHeldStaysBuilding()
    {
        StableState.Write(_project.Root, new StableState("building", NewSha, false, "2026-10-01T00:00:00Z", null));
        using var held = PromoteLock.TryAcquire(_project.Root);

        var entry = await StableInstance.StatusAsync(Manager(), _project.Root, _config);

        Assert.Equal("building", entry.State);
    }

    [Fact]
    public async Task StatusEntryCarriesTheAdditiveStableFields()
    {
        SeedRunningOldBuild();

        var entry = await StableInstance.StatusAsync(Manager(), _project.Root, _config);
        using var doc = JsonDocument.Parse(StatusJson.Serialize([entry]));
        var json = doc.RootElement[0];

        Assert.Equal("stable", json.GetProperty("name").GetString());
        Assert.True(json.GetProperty("running").GetBoolean());
        Assert.True(json.GetProperty("stable").GetBoolean());
        Assert.Equal("ready", json.GetProperty("state").GetString());
        Assert.Equal(OldSha, json.GetProperty("sha").GetString());
        Assert.False(json.GetProperty("dirty").GetBoolean());
        Assert.Equal("2026-10-01T00:00:00Z", json.GetProperty("at").GetString());
        Assert.Equal("https://host/app/", json.GetProperty("link").GetString());
        Assert.False(json.TryGetProperty("message", out _));
    }

    [Fact]
    public void ServiceEntriesGainNoNewFields()
    {
        using var doc = JsonDocument.Parse(StatusJson.Serialize([new ServiceStatusEntry("web", true, 1, null)]));

        Assert.Equal(["name", "running", "pid"], doc.RootElement[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task VerbsWithoutANameLeaveStableAlone()
    {
        var stablePid = SeedRunningOldBuild();
        var web = _project.WriteFile("web.exe", "stub");
        var config = new RunConfig { Services = [new ServiceConfig("web", web, [], null, null)], Stable = _config };
        var manager = Manager();

        var (targets, error) = RunTargets.Select(config, null, _project.Root);
        Assert.Null(error);
        manager.Stop(targets!);
        await manager.StartAsync(targets!);

        Assert.DoesNotContain(stablePid, _spawner.KillTreeCalls);
        Assert.DoesNotContain(_spawner.StartCalls, c => c.Executable.StartsWith(Current));
        Assert.Equal(["web"], targets!.Select(t => t.Name));
    }

    [Fact]
    public void NameStableSelectsTheStableInstance()
    {
        var config = new RunConfig { Stable = _config };

        var (targets, _) = RunTargets.Select(config, "stable", _project.Root);

        var target = Assert.Single(targets!);
        Assert.Equal(Path.Combine(Current, "App.exe"), target.Command);
    }

    [Fact]
    public void NameStableWithoutStableBlockIsUnknown()
    {
        var (targets, error) = RunTargets.Select(new RunConfig(), "stable", _project.Root);

        Assert.Null(targets);
        Assert.Contains("unknown service 'stable'", error);
    }
}
