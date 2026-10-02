using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Watch;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>Pure logic tests against a FakeWatchProcessSpawner — no real OS process involved, so
/// these run on any OS even though WindowsWatchControl is only wired up on Windows in Program.cs.</summary>
public class WindowsWatchControlTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IsRunning_false_when_no_lock_file()
    {
        using var project = new TempProject();
        var control = new WindowsWatchControl(new WatchClientConfig(), new FakeWatchProcessSpawner());

        Assert.False(await control.IsRunningAsync(project.Root));
    }

    [Fact]
    public async Task IsRunning_true_when_lock_pid_is_alive_with_matching_start_time()
    {
        using var project = new TempProject();
        project.WriteWatchLock(4242, StartTime);
        var spawner = new FakeWatchProcessSpawner();
        spawner.Processes[4242] = new WatchProcessSnapshot(4242, StartTime);
        var control = new WindowsWatchControl(new WatchClientConfig(), spawner);

        Assert.True(await control.IsRunningAsync(project.Root));
    }

    [Fact]
    public async Task IsRunning_false_when_lock_pid_no_longer_exists()
    {
        using var project = new TempProject();
        project.WriteWatchLock(4242, StartTime);
        var control = new WindowsWatchControl(new WatchClientConfig(), new FakeWatchProcessSpawner());

        Assert.False(await control.IsRunningAsync(project.Root));
    }

    [Fact]
    public async Task IsRunning_false_when_pid_was_reused_by_a_different_process()
    {
        using var project = new TempProject();
        project.WriteWatchLock(4242, StartTime);
        var spawner = new FakeWatchProcessSpawner();
        spawner.Processes[4242] = new WatchProcessSnapshot(4242, StartTime.AddHours(3)); // different process, PID reused
        var control = new WindowsWatchControl(new WatchClientConfig(), spawner);

        Assert.False(await control.IsRunningAsync(project.Root));
    }

    [Fact]
    public async Task Start_spawns_mf_watch_with_the_project_path_when_not_already_running()
    {
        using var project = new TempProject();
        var spawner = new FakeWatchProcessSpawner();
        var control = new WindowsWatchControl(new WatchClientConfig { Command = "mf-watch" }, spawner);

        var result = await control.StartAsync(project.Root);

        Assert.True(result.Success);
        var call = Assert.Single(spawner.StartCalls);
        Assert.Equal("mf-watch", call.Executable);
        Assert.Equal(["--project", project.Root], call.Arguments);
    }

    [Fact]
    public async Task Start_is_a_noop_when_already_running()
    {
        using var project = new TempProject();
        project.WriteWatchLock(4242, StartTime);
        var spawner = new FakeWatchProcessSpawner();
        spawner.Processes[4242] = new WatchProcessSnapshot(4242, StartTime);
        var control = new WindowsWatchControl(new WatchClientConfig(), spawner);

        var result = await control.StartAsync(project.Root);

        Assert.True(result.Success);
        Assert.Empty(spawner.StartCalls);
    }

    [Fact]
    public async Task Stop_kills_the_tracked_pid_when_it_matches_the_lock()
    {
        using var project = new TempProject();
        project.WriteWatchLock(4242, StartTime);
        var spawner = new FakeWatchProcessSpawner();
        spawner.Processes[4242] = new WatchProcessSnapshot(4242, StartTime);
        var control = new WindowsWatchControl(new WatchClientConfig(), spawner);

        var result = await control.StopAsync(project.Root);

        Assert.True(result.Success);
        Assert.Equal([4242], spawner.KillCalls);
    }

    [Fact]
    public async Task Stop_is_a_noop_when_no_lock_file()
    {
        using var project = new TempProject();
        var spawner = new FakeWatchProcessSpawner();
        var control = new WindowsWatchControl(new WatchClientConfig(), spawner);

        var result = await control.StopAsync(project.Root);

        Assert.True(result.Success);
        Assert.Empty(spawner.KillCalls);
    }

    [Fact]
    public async Task Stop_does_not_kill_a_reused_pid()
    {
        using var project = new TempProject();
        project.WriteWatchLock(4242, StartTime);
        var spawner = new FakeWatchProcessSpawner();
        spawner.Processes[4242] = new WatchProcessSnapshot(4242, StartTime.AddHours(3)); // different process, PID reused
        var control = new WindowsWatchControl(new WatchClientConfig(), spawner);

        var result = await control.StopAsync(project.Root);

        Assert.True(result.Success);
        Assert.Empty(spawner.KillCalls);
    }
}
