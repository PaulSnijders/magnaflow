using MagnaFlow.MfWatch.Config;
using MagnaFlow.MfWatch.Infrastructure;
using MagnaFlow.MfWatch.Polling;
using MagnaFlow.MfWatch.Watch;

namespace MagnaFlow.MfWatch.Tests;

public class WatchLoopTests
{
    private static WatchConfig Config(bool gitSync = false) => new() { GitSync = gitSync };

    [Fact]
    public async Task EmptyPollReportsNoActivity()
    {
        using var project = new TempProject();
        var git = new FakeGitClient();
        var processes = new FakeProcessRunner();
        var notifier = new FakeNotifier();
        var log = new List<string>();
        var loop = new WatchLoop(Config(), git, processes, project.Root, log.Add, notifier);

        var activity = await loop.PollOnceAsync();

        Assert.False(activity);
        Assert.Empty(processes.ExecutableCalls);
        Assert.Empty(notifier.Notifications);
    }

    [Fact]
    public async Task ReadyCommandIsRunAndDoneIsNotified()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready", title: "Say hello");
        var git = new FakeGitClient();
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, _) =>
            {
                File.WriteAllText(project.CmdPath("0001-hello"), TempProject.CmdMarkdown(status: "done", title: "Say hello"));
                return new ProcessResult(0, "", "", false);
            },
        };
        var notifier = new FakeNotifier();
        var log = new List<string>();
        var loop = new WatchLoop(Config(), git, processes, project.Root, log.Add, notifier);

        var activity = await loop.PollOnceAsync();

        Assert.True(activity);
        var call = Assert.Single(processes.ExecutableCalls);
        Assert.Equal("mf-worker", call.Executable);
        Assert.Equal(["run", "--project", project.Root, "0001-hello"], call.Arguments);

        var notification = Assert.Single(notifier.Notifications);
        Assert.Contains("0001-hello", notification.Title);
        Assert.Contains("done", notification.Title);
    }

    [Fact]
    public async Task FollowUpSuffixedCommandIsDispatchedIdenticallyToAPlainOne()
    {
        using var project = new TempProject();
        project.WriteCmd("0005B-round2", status: "ready", title: "Round 2");
        var git = new FakeGitClient();
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, _) =>
            {
                File.WriteAllText(project.CmdPath("0005B-round2"), TempProject.CmdMarkdown(status: "done", title: "Round 2"));
                return new ProcessResult(0, "", "", false);
            },
        };
        var notifier = new FakeNotifier();
        var log = new List<string>();
        var loop = new WatchLoop(Config(), git, processes, project.Root, log.Add, notifier);

        var activity = await loop.PollOnceAsync();

        Assert.True(activity);
        var call = Assert.Single(processes.ExecutableCalls);
        Assert.Equal(["run", "--project", project.Root, "0005B-round2"], call.Arguments);
    }

    [Fact]
    public async Task WorkerArgsAreAppendedAfterFixedArgs()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready");
        var git = new FakeGitClient();
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, _) =>
            {
                File.WriteAllText(project.CmdPath("0001-hello"), TempProject.CmdMarkdown(status: "done"));
                return new ProcessResult(0, "", "", false);
            },
        };
        var config = new WatchConfig { WorkerCommand = "stub-worker", WorkerArgs = ["--flag"] };
        var loop = new WatchLoop(config, git, processes, project.Root, _ => { }, new FakeNotifier());

        await loop.PollOnceAsync();

        var call = Assert.Single(processes.ExecutableCalls);
        Assert.Equal("stub-worker", call.Executable);
        Assert.Equal(["run", "--project", project.Root, "0001-hello", "--flag"], call.Arguments);
    }

    [Fact]
    public async Task QuestionsStatusIsNotifiedDistinctly()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready");
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, _) =>
            {
                File.WriteAllText(project.CmdPath("0001-hello"), TempProject.CmdMarkdown(status: "questions"));
                return new ProcessResult(0, "", "", false);
            },
        };
        var notifier = new FakeNotifier();
        var loop = new WatchLoop(Config(), new FakeGitClient(), processes, project.Root, _ => { }, notifier);

        await loop.PollOnceAsync();

        var notification = Assert.Single(notifier.Notifications);
        Assert.Contains("questions", notification.Title);
    }

    [Fact]
    public async Task AbortedStatusIsNotifiedAsFinished()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready");
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, _) =>
            {
                File.WriteAllText(project.CmdPath("0001-hello"), TempProject.CmdMarkdown(status: "aborted"));
                return new ProcessResult(1, "", "", false);
            },
        };
        var notifier = new FakeNotifier();
        var loop = new WatchLoop(Config(), new FakeGitClient(), processes, project.Root, _ => { }, notifier);

        var activity = await loop.PollOnceAsync();

        Assert.True(activity);
        var notification = Assert.Single(notifier.Notifications);
        Assert.Contains("aborted", notification.Title);
    }

    [Fact]
    public async Task UnexpectedExitCodeIsNotifiedAsError()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready");
        // Worker crashes hard (usage/environment error) without transitioning status at all.
        var processes = new FakeProcessRunner
        {
            ExecutableResults = new Queue<ProcessResult>([new ProcessResult(4, "", "boom", false)]),
        };
        var notifier = new FakeNotifier();
        var loop = new WatchLoop(Config(), new FakeGitClient(), processes, project.Root, _ => { }, notifier);

        await loop.PollOnceAsync();

        var notification = Assert.Single(notifier.Notifications);
        Assert.Contains("error", notification.Title);
    }

    [Fact]
    public async Task WorkerCrashLeavingStatusStuckRunningIsNotifiedAsErrorAndNotAgainNextPoll()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready");
        // Worker crashes mid-run: exits 0 (killed externally, no exit-code signal) but never
        // transitioned status out of "running" before dying.
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, _) =>
            {
                File.WriteAllText(project.CmdPath("0001-hello"), TempProject.CmdMarkdown(status: "running"));
                return new ProcessResult(0, "", "", false);
            },
        };
        var notifier = new FakeNotifier();
        var loop = new WatchLoop(Config(), new FakeGitClient(), processes, project.Root, _ => { }, notifier);

        await loop.PollOnceAsync();
        Assert.Single(notifier.Notifications);
        Assert.Contains("error", notifier.Notifications[0].Title);

        // Next poll: the file is still "running" but mf-watch already surfaced it — no repeat.
        await loop.PollOnceAsync();
        Assert.Single(notifier.Notifications);
    }

    [Fact]
    public async Task RunningStatusFoundOnStartupIsNotifiedOnceNotEveryPoll()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-stale", status: "running");
        var notifier = new FakeNotifier();
        var loop = new WatchLoop(Config(), new FakeGitClient(), new FakeProcessRunner(), project.Root, _ => { }, notifier);

        await loop.PollOnceAsync();
        await loop.PollOnceAsync();
        await loop.PollOnceAsync();

        var notification = Assert.Single(notifier.Notifications);
        Assert.Contains("stale running", notification.Title);
    }

    [Fact]
    public async Task GitSyncPullsBeforeScanAndPushesAfter()
    {
        using var project = new TempProject();
        var git = new FakeGitClient();
        git.PullResults.Enqueue(true);
        var loop = new WatchLoop(Config(gitSync: true), git, new FakeProcessRunner(), project.Root, _ => { }, new FakeNotifier());

        var activity = await loop.PollOnceAsync();

        Assert.True(activity); // pull brought commits counts as activity even with nothing to run
        Assert.Equal(["pull (brought-commits=True)", "push"], git.Operations);
    }

    [Fact]
    public async Task GitSyncPushRunsEvenWithoutActivity()
    {
        using var project = new TempProject();
        var git = new FakeGitClient(); // no pulled commits, nothing ready
        var loop = new WatchLoop(Config(gitSync: true), git, new FakeProcessRunner(), project.Root, _ => { }, new FakeNotifier());

        var activity = await loop.PollOnceAsync();

        Assert.False(activity);
        Assert.Contains("push", git.Operations);
    }

    [Fact]
    public async Task GitPullFailureIsNotifiedButPollContinues()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready");
        var git = new FakeGitClient { PullFails = true };
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, _) =>
            {
                File.WriteAllText(project.CmdPath("0001-hello"), TempProject.CmdMarkdown(status: "done"));
                return new ProcessResult(0, "", "", false);
            },
        };
        var notifier = new FakeNotifier();
        var loop = new WatchLoop(Config(gitSync: true), git, processes, project.Root, _ => { }, notifier);

        var activity = await loop.PollOnceAsync();

        Assert.True(activity); // the run still happened despite the pull failure
        Assert.Contains(notifier.Notifications, n => n.Title.Contains("git pull failed"));
        Assert.Single(processes.ExecutableCalls); // scan/run still proceeded on-disk
    }

    [Fact]
    public async Task GitPushFailureIsNotified()
    {
        using var project = new TempProject();
        var git = new FakeGitClient { PushFails = true };
        var notifier = new FakeNotifier();
        var loop = new WatchLoop(Config(gitSync: true), git, new FakeProcessRunner(), project.Root, _ => { }, notifier);

        await loop.PollOnceAsync();

        Assert.Contains(notifier.Notifications, n => n.Title.Contains("git push failed"));
    }

    [Fact]
    public async Task MultipleReadyCommandsRunSequentiallyInIdOrder()
    {
        using var project = new TempProject();
        project.WriteCmd("0002-second", status: "ready");
        project.WriteCmd("0001-first", status: "ready");
        var order = new List<string>();
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, args) =>
            {
                var id = args[3];
                order.Add(id);
                File.WriteAllText(project.CmdPath(id), TempProject.CmdMarkdown(status: "done"));
                return new ProcessResult(0, "", "", false);
            },
        };
        var loop = new WatchLoop(Config(), new FakeGitClient(), processes, project.Root, _ => { }, new FakeNotifier());

        await loop.PollOnceAsync();

        Assert.Equal(["0001-first", "0002-second"], order);
    }

    private static BackoffScheduler Scheduler() =>
        new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), new FakeClock());

    [Fact]
    public async Task ShutdownDuringDispatchLetsTheWorkerFinishThenTheLoopExits()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-first", status: "ready", title: "First");
        project.WriteCmd("0002-second", status: "ready", title: "Second");
        using var shutdown = new CancellationTokenSource();
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, args) =>
            {
                // Ctrl+C arrives while the worker runs; the worker still completes its command.
                shutdown.Cancel();
                File.WriteAllText(project.CmdPath(args[3]), TempProject.CmdMarkdown(status: "done", title: "First"));
                return new ProcessResult(0, "", "", false);
            },
        };
        var notifier = new FakeNotifier();
        var git = new FakeGitClient();
        var loop = new WatchLoop(Config(gitSync: true), git, processes, project.Root, _ => { }, notifier);

        var run = loop.RunAsync(Scheduler(), once: false, shutdown.Token);
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(run, finished);
        await run; // exits cleanly, no OperationCanceledException
        var call = Assert.Single(processes.ExecutableCalls); // no new dispatch after the shutdown
        Assert.Equal("0001-first", call.Arguments[3]);
        Assert.False(Assert.Single(processes.ExecutableTokens).CanBeCanceled); // shutdown never reached the worker
        Assert.Contains(notifier.Notifications, n => n.Title == "0001-first: done");
        Assert.Equal("push", git.Operations[^1]); // the poll still ends with its push
        Assert.False(loop.IsDispatching);
    }

    [Fact]
    public async Task ShutdownWhileIdleExitsAtOnce()
    {
        using var project = new TempProject();
        using var shutdown = new CancellationTokenSource();
        var sleeping = new TaskCompletionSource();
        var loop = new WatchLoop(Config(), new FakeGitClient(), new FakeProcessRunner(), project.Root,
            line => { if (line.StartsWith("sleeping", StringComparison.Ordinal)) sleeping.TrySetResult(); }, new FakeNotifier());

        var run = loop.RunAsync(Scheduler(), once: false, shutdown.Token);
        await sleeping.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.CancelAsync();
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(run, finished); // the one-minute sleep was cut short
        await run;
    }

    [Fact]
    public async Task WorkerSpawnIsMarkedAsDispatching()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready");
        WatchLoop? loop = null;
        var seen = false;
        var processes = new FakeProcessRunner
        {
            OnRunExecutable = (_, _) =>
            {
                seen = loop!.IsDispatching;
                File.WriteAllText(project.CmdPath("0001-hello"), TempProject.CmdMarkdown(status: "done"));
                return new ProcessResult(0, "", "", false);
            },
        };
        loop = new WatchLoop(Config(), new FakeGitClient(), processes, project.Root, _ => { }, new FakeNotifier());

        await loop.PollOnceAsync();

        Assert.True(seen);
        Assert.False(loop.IsDispatching);
    }
}
