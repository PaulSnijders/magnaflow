using MagnaFlow.WorkerController.Actions;
using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Infrastructure;

namespace MagnaFlow.WorkerController.Tests;

public class ActionRunnerTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly FakeGitClient _git = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly FakeAgentRunner _agent = new();
    private readonly List<string> _output = [];
    private readonly List<string> _errors = [];
    private DateTimeOffset _now = new(2026, 10, 13, 9, 0, 0, TimeSpan.FromHours(2));

    public ActionRunnerTests()
    {
        _project.WriteFile(".magnaflow/actions.yml", """
            actions:
              hello:
                script: tools/hello
                args: ["a", "b"]
                timeout_minutes: 7
              ask:
                prompt: Resolve the conflict.
            """);
        _project.WriteFile("tools/hello", "#!/bin/sh\necho hi\n");
        _project.WriteFile(".magnaflow/config.yml", "build:\n  command: b\ntest:\n  command: t\n");
    }

    public void Dispose() => _project.Dispose();

    private ActionRunner Runner() =>
        new(_git, _processes, _ => _agent, _project.Root, _output.Add, _errors.Add, () => _now, isWindows: false);

    private string LogPath(string name) => ActionLog.PathFor(_project.Root, name);

    [Fact]
    public async Task Unknown_name_is_a_usage_error()
    {
        var exit = await Runner().RunAsync("nope", "manual");

        Assert.Equal(ExitCodes.UsageError, exit);
        Assert.Contains("unknown action 'nope'", _errors.Single());
        Assert.Empty(_processes.ExecutableCalls);
    }

    [Fact]
    public async Task A_missing_script_is_a_usage_error()
    {
        File.Delete(Path.Combine(_project.Root, "tools", "hello"));

        var exit = await Runner().RunAsync("hello", "manual");

        Assert.Equal(ExitCodes.UsageError, exit);
        Assert.Contains("script not found", _errors.Single());
    }

    [Fact]
    public async Task A_running_command_refuses_without_running_or_logging()
    {
        _project.WriteCmd("0001-busy", TempProject.CmdMarkdown(status: "running"));

        var exit = await Runner().RunAsync("hello", "schedule");

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Equal(["refused: a command is running (0001-busy)"], _output);
        Assert.Empty(_processes.ExecutableCalls);
        Assert.False(File.Exists(LogPath("hello")));
        var last = ActionLastRun.Load(_project.Root, "hello");
        Assert.Equal("a command is running (0001-busy)", last.Refused);
        Assert.Equal(_now, last.RefusedAt);
        Assert.Null(last.RunAt);
    }

    [Fact]
    public async Task A_dirty_tree_refuses_and_keeps_the_last_run()
    {
        new ActionLastRun(_now.AddHours(-1), "manual", "ok", 3, null, null).Save(_project.Root, "hello");
        _git.Clean = false;

        var exit = await Runner().RunAsync("hello", "manual");

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Empty(_processes.ExecutableCalls);
        Assert.False(File.Exists(LogPath("hello")));
        var last = ActionLastRun.Load(_project.Root, "hello");
        Assert.Equal("the working tree is dirty", last.Refused);
        Assert.Equal("ok", last.Result);
        Assert.Equal(_now.AddHours(-1), last.RunAt);
    }

    [Fact]
    public async Task A_script_that_succeeds_is_logged_and_recorded()
    {
        new ActionLastRun(null, null, null, null, _now.AddHours(-1), "the working tree is dirty").Save(_project.Root, "hello");
        _processes.OnExecutable = _ => _now = _now.AddSeconds(4);

        var exit = await Runner().RunAsync("hello", "schedule");

        Assert.Equal(ExitCodes.Success, exit);
        var call = _processes.ExecutableCalls.Single();
        Assert.Equal(Path.Combine(_project.Root, "tools", "hello"), call.Executable);
        Assert.Equal(["a", "b"], call.Arguments);
        var log = File.ReadAllText(LogPath("hello"));
        Assert.StartsWith("=== action hello (schedule) — 2026-10-13T09:00:00 ===", log);
        Assert.Contains("=== result: ok — 4s ===", log);
        Assert.NotEqual(0xEF, File.ReadAllBytes(LogPath("hello"))[0]); // no BOM
        var last = ActionLastRun.Load(_project.Root, "hello");
        Assert.Equal(("schedule", "ok", 4), (last.Trigger, last.Result, last.DurationSeconds));
        Assert.Null(last.Refused); // a run newer than the refusal clears it
        Assert.Equal("action hello (schedule): started", _output[0]);
    }

    [Fact]
    public async Task The_log_is_appended_run_after_run()
    {
        await Runner().RunAsync("hello", "manual");
        await Runner().RunAsync("hello", "manual");

        var headers = File.ReadAllLines(LogPath("hello")).Count(l => l.StartsWith("=== action hello"));
        Assert.Equal(2, headers);
    }

    [Fact]
    public async Task A_failing_script_is_failed_with_its_exit_code()
    {
        _processes.ExecutableResults.Enqueue(new ProcessResult(5, "", "", false));

        var exit = await Runner().RunAsync("hello", "manual");

        Assert.Equal(ExitCodes.TaskFailed, exit);
        Assert.Contains("=== result: failed (exit 5)", File.ReadAllText(LogPath("hello")));
        Assert.Equal("failed (exit 5)", ActionLastRun.Load(_project.Root, "hello").Result);
    }

    [Fact]
    public async Task A_script_that_times_out_is_timeout()
    {
        _processes.ExecutableResults.Enqueue(new ProcessResult(-1, "", "", TimedOut: true));

        var exit = await Runner().RunAsync("hello", "manual");

        Assert.Equal(ExitCodes.TaskFailed, exit);
        Assert.Equal("timeout", ActionLastRun.Load(_project.Root, "hello").Result);
    }

    [Fact]
    public async Task A_tree_left_dirty_is_failed()
    {
        _processes.OnExecutable = _ => _git.Clean = false;

        var exit = await Runner().RunAsync("hello", "manual");

        Assert.Equal(ExitCodes.TaskFailed, exit);
        Assert.Contains("=== result: failed (left the tree dirty)", File.ReadAllText(LogPath("hello")));
    }

    [Fact]
    public async Task A_prompt_is_sent_as_is_without_a_resume_and_logs_its_final_text()
    {
        _agent.Results.Enqueue(new AgentResult(0, "s-1", "All conflicts resolved.", TimedOut: false));

        var exit = await Runner().RunAsync("ask", "manual");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["Resolve the conflict."], _agent.Prompts);
        Assert.Equal([null], _agent.ResumeIds);
        var lines = File.ReadAllLines(LogPath("ask"));
        Assert.Contains("{\"fake\":\"line 1\"}", lines);
        Assert.Equal("All conflicts resolved.", lines[^2]);
        Assert.StartsWith("=== result: ok", lines[^1]);
    }

    [Fact]
    public async Task A_prompt_action_needs_a_valid_config()
    {
        File.Delete(Path.Combine(_project.Root, ".magnaflow", "config.yml"));

        var exit = await Runner().RunAsync("ask", "manual");

        Assert.Equal(ExitCodes.UsageError, exit);
        Assert.Empty(_agent.Prompts);
    }

    [Fact]
    public async Task An_unavailable_agent_is_an_environment_error()
    {
        _agent.Available = false;

        Assert.Equal(ExitCodes.EnvironmentError, await Runner().RunAsync("ask", "manual"));
    }
}
