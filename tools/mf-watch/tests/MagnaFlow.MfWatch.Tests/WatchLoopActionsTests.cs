using MagnaFlow.MfWatch.Actions;
using MagnaFlow.MfWatch.Config;
using MagnaFlow.MfWatch.Infrastructure;
using MagnaFlow.MfWatch.Watch;

namespace MagnaFlow.MfWatch.Tests;

/// <summary>Poll step 5: project actions (docs/specs/watch/mf-watch.md#one-poll).</summary>
public class WatchLoopActionsTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly FakeGitClient _git = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeClock _clock = new();
    private readonly List<string> _log = [];

    /// <summary>Everything in order: pulls, pushes (from the fake git) and spawns.</summary>
    private readonly List<string> _events = [];

    /// <summary>Per action name: what the fake worker answers. Default: exit 0, and it records a run.</summary>
    private readonly Dictionary<string, Func<ProcessResult>> _actionResults = [];

    public WatchLoopActionsTests()
    {
        _git.Operations = _events;
        _processes.OnRunExecutable = (_, args) =>
        {
            if (args[0] == "run")
            {
                _events.Add($"run {args[3]}");
                File.WriteAllText(_project.CmdPath(args[3]), TempProject.CmdMarkdown(status: "done"));
                return new ProcessResult(0, "", "", false);
            }
            var name = args[3];
            _events.Add($"action {name} {args[5]}");
            if (_actionResults.TryGetValue(name, out var result))
                return result();
            RecordRun(name, _clock.UtcNow);
            return new ProcessResult(0, "", "", false);
        };
    }

    public void Dispose() => _project.Dispose();

    private WatchLoop Loop(bool gitSync = false) =>
        new(new WatchConfig { GitSync = gitSync }, _git, _processes, _project.Root, _log.Add, _notifier, _clock, TimeZoneInfo.Utc);

    private void WriteActions(string yaml) => _project.WriteFile(".magnaflow/actions.yml", yaml);

    private void Request(string name) => _project.WriteFile($".magnaflow/actions/{name}.request", "");

    private bool RequestExists(string name) => File.Exists(WatchActions.RequestPath(_project.Root, name));

    private void RecordRun(string name, DateTimeOffset at) =>
        _project.WriteFile($".magnaflow/actions/{name}.last.yml", $"run_at: {at:yyyy-MM-ddTHH:mm:sszzz}\nresult: ok\n");

    private static ProcessResult Refused(string reason) => new(3, $"refused: {reason}\n", "", false);

    private const string Board = """
        actions:
          board:
            script: tools/board
            every_minutes: 10
          fix:
            prompt: fix it
        """;

    [Fact]
    public async Task Without_actions_yml_the_poll_is_as_before()
    {
        _project.WriteCmd("0001-a");
        Request("fix"); // a stray request without actions.yml is left alone

        var loop = Loop(gitSync: true);
        await loop.PollOnceAsync();

        Assert.Equal(["pull (brought-commits=False)", "run 0001-a", "push"], _events);
        Assert.Null(loop.NextActionDue);
        Assert.Equal(TimeSpan.FromMinutes(7), loop.SleepFor(TimeSpan.FromMinutes(7)));
        Assert.Equal(0, _git.HeadReads);
        Assert.DoesNotContain(_log, l => l.Contains("action"));
        Assert.True(RequestExists("fix"));
    }

    [Fact]
    public async Task Requests_run_before_scheduled_ones_after_dispatch_and_before_the_push()
    {
        WriteActions(Board);
        _project.WriteCmd("0001-a");
        Request("fix");

        await Loop(gitSync: true).PollOnceAsync();

        Assert.Equal(["pull (brought-commits=False)", "run 0001-a", "action fix manual", "action board schedule", "push"], _events);
        Assert.False(RequestExists("fix"));
        var call = _processes.ExecutableCalls.First(c => c.Arguments[0] == "action");
        Assert.Equal(["action", "--project", _project.Root, "fix", "--trigger", "manual"], call.Arguments);
    }

    [Fact]
    public async Task After_a_failed_pull_a_request_runs_but_a_scheduled_action_does_not()
    {
        WriteActions(Board);
        Request("fix");
        _git.PullFails = true;

        var loop = Loop(gitSync: true);
        await loop.PollOnceAsync();

        Assert.Equal(["pull (failed)", "action fix manual"], _events);
        // The due board action waits for a synced poll; it does not cut the sleep to nothing.
        Assert.Null(loop.NextActionDue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(2)]
    public async Task The_request_is_deleted_whatever_the_outcome(int exit)
    {
        WriteActions(Board);
        Request("fix");
        _actionResults["fix"] = () => exit == 3 ? Refused("the working tree is dirty") : new ProcessResult(exit, "", "", false);

        await Loop().PollOnceAsync();

        Assert.False(RequestExists("fix"));
    }

    [Fact]
    public async Task A_request_for_an_unknown_action_is_deleted_and_logged()
    {
        WriteActions(Board);
        Request("ghost");

        await Loop().PollOnceAsync();

        Assert.False(RequestExists("ghost"));
        Assert.DoesNotContain(_events, e => e.StartsWith("action ghost"));
        Assert.Contains("[action ghost] request skipped: no such action in actions.yml", _log);
    }

    [Fact]
    public async Task A_scheduled_action_runs_when_due_and_cuts_the_sleep_to_its_next_due_time()
    {
        WriteActions(Board);
        var loop = Loop();

        await loop.PollOnceAsync();
        Assert.Equal(["action board schedule"], _events);
        Assert.Equal(_clock.UtcNow.AddMinutes(10), loop.NextActionDue);
        Assert.Equal(TimeSpan.FromMinutes(10), loop.SleepFor(TimeSpan.FromMinutes(15)));
        Assert.Equal(TimeSpan.FromMinutes(1), loop.SleepFor(TimeSpan.FromMinutes(1)));

        _clock.Advance(TimeSpan.FromMinutes(4));
        await loop.PollOnceAsync();
        Assert.Single(_events); // not due yet
        Assert.Equal(TimeSpan.FromMinutes(6), loop.SleepFor(TimeSpan.FromMinutes(15)));

        _clock.Advance(TimeSpan.FromMinutes(6));
        await loop.PollOnceAsync();
        Assert.Equal(2, _events.Count);
    }

    [Fact]
    public async Task A_refused_action_does_not_shorten_the_sleep_and_its_refusal_is_logged_once()
    {
        WriteActions(Board);
        _actionResults["board"] = () => Refused("the working tree is dirty");
        var loop = Loop();

        await loop.PollOnceAsync();
        await loop.PollOnceAsync();

        Assert.Equal(2, _events.Count); // retried at the next regular poll
        Assert.Null(loop.NextActionDue);
        Assert.Equal(TimeSpan.FromMinutes(15), loop.SleepFor(TimeSpan.FromMinutes(15)));
        Assert.Single(_log, l => l == "[action board] refused: the working tree is dirty");
        Assert.Empty(_notifier.Notifications);

        _actionResults["board"] = () => Refused("a command is running (0001-a)");
        await loop.PollOnceAsync();
        Assert.Contains("[action board] refused: a command is running (0001-a)", _log);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(-1, true)]
    public async Task A_failed_or_unexpected_exit_is_notified(int exit, bool timedOut)
    {
        WriteActions(Board);
        Request("fix");
        _actionResults["fix"] = () => new ProcessResult(exit, "", "", timedOut);

        await Loop().PollOnceAsync();

        Assert.Contains(_notifier.Notifications, n => n.Title == "action fix: failed");
    }

    [Fact]
    public async Task An_action_that_moved_head_is_activity()
    {
        WriteActions("actions:\n  fix:\n    prompt: x\n");
        Request("fix");
        _actionResults["fix"] = () => { _git.Head = "h1"; return new ProcessResult(0, "", "", false); };

        Assert.True(await Loop().PollOnceAsync());
    }

    [Fact]
    public async Task An_action_that_left_head_alone_is_no_activity()
    {
        WriteActions("actions:\n  fix:\n    prompt: x\n");
        Request("fix");

        Assert.False(await Loop().PollOnceAsync());
    }

    [Fact]
    public async Task An_invalid_actions_yml_is_logged_once_and_requests_are_still_deleted()
    {
        WriteActions("actions:\n  Bad_Name:\n    every_minutes: 1\n");
        Request("fix");
        _project.WriteCmd("0001-a");
        var loop = Loop();

        await loop.PollOnceAsync();
        await loop.PollOnceAsync();

        Assert.Equal(["run 0001-a"], _events);
        Assert.False(RequestExists("fix"));
        Assert.Contains("[action fix] request skipped: actions.yml is invalid", _log);
        Assert.Single(_log, l => l.StartsWith("actions.yml: ") && l.EndsWith("; actions skipped"));
    }

    [Fact]
    public async Task A_shutdown_starts_no_action()
    {
        WriteActions(Board);
        Request("fix");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Loop().PollOnceAsync(cts.Token);

        Assert.Empty(_events);
        Assert.True(RequestExists("fix"));
    }

    [Fact]
    public async Task A_request_ran_just_now_makes_the_same_scheduled_action_not_due()
    {
        WriteActions(Board);
        Request("board");

        await Loop().PollOnceAsync();

        Assert.Equal(["action board manual"], _events);
    }
}
