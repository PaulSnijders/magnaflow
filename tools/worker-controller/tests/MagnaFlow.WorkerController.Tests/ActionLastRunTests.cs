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
    public void Round_trips_a_skip_after_the_run_and_refusal()
    {
        using var project = new TempProject();
        var runAt = new DateTimeOffset(2026, 10, 13, 3, 0, 0, TimeSpan.FromHours(2));
        var last = new ActionLastRun(runAt, "schedule", "ok", 40, runAt.AddHours(1), "the working tree is dirty",
            runAt.AddDays(1), "condition error (exit 2)");

        last.Save(project.Root, "hello");
        var text = File.ReadAllText(ActionLastRun.PathFor(project.Root, "hello"));

        Assert.Equal(last, ActionLastRun.Load(project.Root, "hello"));
        Assert.Contains("skipped: \"condition error (exit 2)\"", text);
        Assert.True(text.IndexOf("refused:", StringComparison.Ordinal) < text.IndexOf("skipped_at:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_record_has_no_skip_fields()
    {
        using var project = new TempProject();
        new ActionLastRun(DateTimeOffset.Now, "manual", "ok", 1, null, null, null, null).Save(project.Root, "hello");

        Assert.DoesNotContain("skipped", File.ReadAllText(ActionLastRun.PathFor(project.Root, "hello")));
    }

    [Fact]
    public void Missing_means_never_run()
    {
        using var project = new TempProject();
        Assert.Equal(ActionLastRun.None, ActionLastRun.Load(project.Root, "hello"));
    }
}
