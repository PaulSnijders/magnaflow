using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Watch;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>Pure logic tests against a FakeProcessRunner — no real systemd involved, so these run
/// on any OS even though LinuxWatchControl is only wired up on Linux in Program.cs.</summary>
public class LinuxWatchControlTests
{
    [Fact]
    public async Task IsRunning_escapes_the_path_and_checks_is_active()
    {
        using var project = new TempProject();
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(0, "GIT-magnaflow-proj\n", "", false)); // systemd-escape
        runner.Results.Enqueue(new ProcessResult(0, "", "", false)); // is-active, exit 0 = active
        var control = new LinuxWatchControl(runner);

        var running = await control.IsRunningAsync(project.Root);

        Assert.True(running);
        Assert.Equal("systemd-escape", runner.Calls[0].Executable);
        Assert.Equal([project.Root], runner.Calls[0].Arguments);
        Assert.Equal("systemctl", runner.Calls[1].Executable);
        Assert.Equal(["--user", "is-active", "--quiet", "mf-watch@GIT-magnaflow-proj.service"], runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task IsRunning_false_when_is_active_exits_nonzero()
    {
        using var project = new TempProject();
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(0, "escaped\n", "", false));
        runner.Results.Enqueue(new ProcessResult(3, "inactive", "", false));
        var control = new LinuxWatchControl(runner);

        Assert.False(await control.IsRunningAsync(project.Root));
    }

    [Fact]
    public async Task Start_enables_and_starts_the_unit()
    {
        using var project = new TempProject();
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(0, "escaped\n", "", false));
        runner.Results.Enqueue(new ProcessResult(0, "", "", false));
        var control = new LinuxWatchControl(runner);

        var result = await control.StartAsync(project.Root);

        Assert.True(result.Success);
        Assert.Equal(["--user", "enable", "--now", "mf-watch@escaped.service"], runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task Stop_disables_and_stops_the_unit()
    {
        using var project = new TempProject();
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(0, "escaped\n", "", false));
        runner.Results.Enqueue(new ProcessResult(0, "", "", false));
        var control = new LinuxWatchControl(runner);

        var result = await control.StopAsync(project.Root);

        Assert.True(result.Success);
        Assert.Equal(["--user", "disable", "--now", "mf-watch@escaped.service"], runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task Start_surfaces_systemctl_failure_output()
    {
        using var project = new TempProject();
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(0, "escaped\n", "", false));
        runner.Results.Enqueue(new ProcessResult(1, "", "Failed to enable unit: no such unit", false));
        var control = new LinuxWatchControl(runner);

        var result = await control.StartAsync(project.Root);

        Assert.False(result.Success);
        Assert.Contains("no such unit", result.Error);
    }
}
