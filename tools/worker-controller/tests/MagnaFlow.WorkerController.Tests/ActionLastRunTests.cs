using MagnaFlow.WorkerController.Actions;

namespace MagnaFlow.WorkerController.Tests;

public class ActionLastRunTests
{
    [Fact]
    public void Round_trips_a_run_and_a_refusal()
    {
        using var project = new TempProject();
        var runAt = new DateTimeOffset(2026, 10, 13, 14, 2, 11, TimeSpan.FromHours(2));
        var refusedAt = runAt.AddMinutes(10);
        var last = new ActionLastRun(runAt, "schedule", "failed (exit 2)", 12, refusedAt, "a command is running (0001-x)");

        last.Save(project.Root, "hello");
        var loaded = ActionLastRun.Load(project.Root, "hello");

        Assert.Equal(last, loaded);
        Assert.False(File.Exists(ActionLastRun.PathFor(project.Root, "hello") + ".tmp"));
    }

    [Fact]
    public void Missing_means_never_run()
    {
        using var project = new TempProject();
        Assert.Equal(ActionLastRun.None, ActionLastRun.Load(project.Root, "hello"));
    }
}
