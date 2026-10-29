using MagnaFlow.MfWatch.Config;
using MagnaFlow.MfWatch.Infrastructure;
using MagnaFlow.MfWatch.Watch;

namespace MagnaFlow.MfWatch.Tests;

/// <summary>git_sync per command (docs/specs/watch/mf-watch.md#one-poll step 4) and the
/// `on_conflict` auto-resolve (#git-sync).</summary>
public class WatchLoopSyncTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly FakeGitClient _git = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly FakeNotifier _notifier = new();
    private readonly List<string> _log = [];

    /// <summary>Everything in order: pulls, pushes (from the fake git) and spawns.</summary>
    private readonly List<string> _events = [];

    /// <summary>The status a worker run leaves; "ready" simulates a refusal.</summary>
    private string _runLeaves = "done";

    /// <summary>What the fake `mf-worker action` does; default exit 0.</summary>
    private Func<ProcessResult> _action = () => new ProcessResult(0, "", "", false);

    public WatchLoopSyncTests()
    {
        _git.Operations = _events;
        _processes.OnRunExecutable = (_, args) =>
        {
            if (args[0] == "run")
            {
                _events.Add($"run {args[3]}");
                File.WriteAllText(_project.CmdPath(args[3]), TempProject.CmdMarkdown(status: _runLeaves));
                return new ProcessResult(_runLeaves == "ready" ? 3 : 0, "", "", false);
            }
            _events.Add($"action {args[3]} {args[5]}");
            return _action();
        };
    }

    public void Dispose() => _project.Dispose();

    private WatchLoop Loop(string? onConflict = WatchConfig.DefaultOnConflict) =>
        new(new WatchConfig { GitSync = true, OnConflict = onConflict, WorkerArgs = ["--verbose"] },
            _git, _processes, _project.Root, _log.Add, _notifier);

    private void WriteActions() => _project.WriteFile(".magnaflow/actions.yml", """
        actions:
          resolve-conflicts:
            prompt: resolve it
        """);

    private static Func<object> Pulls(bool broughtCommits) => () => broughtCommits;
    private static Func<object> Conflicts(params string[] files) => () => files;

    [Fact]
    public async Task Every_command_is_pushed_and_pulled_before_the_next_one_runs()
    {
        _project.WriteCmd("0001-a");
        _project.WriteCmd("0002-b");

        var activity = await Loop().PollOnceAsync();

        Assert.True(activity);
        Assert.Equal([
            "pull (brought-commits=False)",
            "run 0001-a", "push", "pull (brought-commits=False)",
            "run 0002-b", "push", "pull (brought-commits=False)",
            "push"], _events);
    }

    [Fact]
    public async Task A_command_readied_by_the_pull_after_a_run_is_dispatched_in_the_same_poll()
    {
        _project.WriteCmd("0001-a");
        _git.PullScript.Enqueue(Pulls(false));
        _git.PullScript.Enqueue(() =>
        {
            _project.WriteCmd("0002-b"); // pushed from the desktop while 0001 ran
            return true;
        });

        await Loop().PollOnceAsync();

        Assert.Contains("run 0002-b", _events);
    }

    [Fact]
    public async Task A_command_edited_on_the_remote_while_queued_is_run_as_it_is_now()
    {
        _project.WriteCmd("0001-a");
        _project.WriteCmd("0002-b");
        _git.PullScript.Enqueue(Pulls(false));
        _git.PullScript.Enqueue(() =>
        {
            _project.WriteCmd("0002-b", status: "draft"); // taken back on the desktop
            return true;
        });

        await Loop().PollOnceAsync();

        Assert.DoesNotContain("run 0002-b", _events);
    }

    [Fact]
    public async Task A_refusing_worker_is_dispatched_once_per_poll()
    {
        _project.WriteCmd("0001-a");
        _runLeaves = "ready";

        await Loop().PollOnceAsync();

        Assert.Single(_events, e => e == "run 0001-a");
    }

    [Fact]
    public async Task A_failed_pull_after_a_command_stops_dispatch_and_the_final_push()
    {
        _project.WriteCmd("0001-a");
        _project.WriteCmd("0002-b");
        _git.PullScript.Enqueue(Pulls(false));
        _git.PullScript.Enqueue(() => new GitException("offline"));

        await Loop().PollOnceAsync();

        Assert.Equal(["pull (brought-commits=False)", "run 0001-a", "push", "pull (failed)"], _events);
        Assert.Contains(_notifier.Notifications, n => n.Title == "mf-watch: git pull failed");
    }

    [Fact]
    public async Task A_failed_rebase_pull_after_a_rejected_push_stops_dispatch()
    {
        _project.WriteCmd("0001-a");
        _project.WriteCmd("0002-b");
        _git.PullScript.Enqueue(Pulls(false));
        _git.PullScript.Enqueue(() => new GitException("offline"));
        _git.PushOutcomes.Enqueue(PushOutcome.RejectedNonFastForward);

        await Loop().PollOnceAsync();

        Assert.Equal(["pull (brought-commits=False)", "run 0001-a", "push (rejected)", "pull (failed)"], _events);
    }

    [Fact]
    public async Task Without_git_sync_every_ready_command_still_runs_once()
    {
        _project.WriteCmd("0001-a");
        _project.WriteCmd("0002-b");
        var loop = new WatchLoop(new WatchConfig(), _git, _processes, _project.Root, _log.Add, _notifier);

        await loop.PollOnceAsync();

        Assert.Equal(["run 0001-a", "run 0002-b"], _events);
    }

    [Fact]
    public async Task A_conflict_runs_the_on_conflict_action_and_a_clean_second_pull_resolves_it()
    {
        WriteActions();
        _project.WriteCmd("0001-a");
        _git.PullScript.Enqueue(Conflicts("docs/specs/STATUS.md"));
        _git.PullScript.Enqueue(Pulls(true));

        var activity = await Loop().PollOnceAsync();

        Assert.True(activity);
        Assert.Equal([
            "pull (conflict)", "action resolve-conflicts conflict", "pull (brought-commits=True)",
            "run 0001-a", "push", "pull (brought-commits=False)", "push"], _events);
        var call = _processes.ExecutableCalls.First(c => c.Arguments[0] == "action");
        Assert.Equal(["action", "--project", _project.Root, "resolve-conflicts", "--trigger", "conflict", "--verbose"], call.Arguments);
        Assert.Contains(_log, l => l == "conflict in docs/specs/STATUS.md resolved by action resolve-conflicts");
        var resolved = Assert.Single(_notifier.Notifications, n => n.Title == "mf-watch: git conflict resolved");
        Assert.Contains("docs/specs/STATUS.md", resolved.Message);
        Assert.DoesNotContain(_notifier.Notifications, n => n.Title == "mf-watch: git pull conflict");
    }

    [Fact]
    public async Task A_conflict_on_the_pull_after_a_command_is_auto_resolved_too()
    {
        WriteActions();
        _project.WriteCmd("0001-a");
        _project.WriteCmd("0002-b");
        _git.PullScript.Enqueue(Pulls(false));
        _git.PullScript.Enqueue(Conflicts("docs/a.md"));
        _git.PullScript.Enqueue(Pulls(true));

        await Loop().PollOnceAsync();

        Assert.Equal([
            "pull (brought-commits=False)", "run 0001-a", "push",
            "pull (conflict)", "action resolve-conflicts conflict", "pull (brought-commits=True)",
            "run 0002-b", "push", "pull (brought-commits=False)", "push"], _events);
    }

    [Fact]
    public async Task An_unresolved_conflict_is_notified_with_the_log_and_not_retried_in_the_same_state()
    {
        WriteActions();
        _project.WriteCmd("0001-a");
        _git.PullConflict = ["docs/a.md"];
        _action = () => new ProcessResult(0, "", "", false); // the agent gave up and aborted cleanly

        var loop = Loop();
        await loop.PollOnceAsync();
        await loop.PollOnceAsync();

        Assert.Equal(["pull (conflict)", "action resolve-conflicts conflict", "pull (conflict)", "pull (conflict)"], _events);
        var notification = Assert.Single(_notifier.Notifications);
        Assert.Equal("mf-watch: git pull conflict", notification.Title);
        Assert.Contains("docs/a.md", notification.Message);
        Assert.Contains("action resolve-conflicts did not resolve it; see .magnaflow/actions/resolve-conflicts.log", notification.Message);
        Assert.Contains(_log, l => l.Contains("still failing"));

        _git.SyncState = "local1..remote2"; // the remote moved: a new state gets a new attempt
        await loop.PollOnceAsync();
        Assert.Equal(2, _events.Count(e => e.StartsWith("action ")));
    }

    [Fact]
    public async Task A_refused_conflict_action_leaves_the_conflict_notified()
    {
        WriteActions();
        _git.PullConflict = ["docs/a.md"];
        _action = () => new ProcessResult(3, "refused: the working tree is dirty\n", "", false);

        await Loop().PollOnceAsync();

        var notification = Assert.Single(_notifier.Notifications);
        Assert.Equal("mf-watch: git pull conflict", notification.Title);
        Assert.Contains(_log, l => l == "[action resolve-conflicts] refused: the working tree is dirty");
    }

    [Fact]
    public async Task With_on_conflict_off_a_conflict_is_handled_as_before()
    {
        WriteActions();
        _git.PullConflict = ["docs/a.md"];

        await Loop(onConflict: null).PollOnceAsync();

        AssertPlainConflict();
    }

    [Fact]
    public async Task Without_actions_yml_a_conflict_is_handled_as_before()
    {
        _git.PullConflict = ["docs/a.md"];

        await Loop().PollOnceAsync();

        AssertPlainConflict();
    }

    [Fact]
    public async Task An_on_conflict_name_missing_from_actions_yml_is_handled_as_before()
    {
        WriteActions();
        _git.PullConflict = ["docs/a.md"];

        await Loop(onConflict: "fix-it").PollOnceAsync();

        AssertPlainConflict();
    }

    private void AssertPlainConflict()
    {
        Assert.Equal(["pull (conflict)"], _events);
        var notification = Assert.Single(_notifier.Notifications);
        Assert.Equal("mf-watch: git pull conflict", notification.Title);
        Assert.Contains("resolved by hand", notification.Message);
        Assert.Contains(_log, l => l.Contains("conflict in docs/a.md; rebase aborted, tree unchanged"));
    }
}
