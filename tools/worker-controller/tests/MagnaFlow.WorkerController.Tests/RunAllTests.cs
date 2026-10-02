using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Execution;
using MagnaFlow.WorkerController.Infrastructure;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class RunAllTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly FakeGitClient _git = new();
    private readonly FakeAgentRunner _agent = new();
    private readonly FakeProcessRunner _processes = new();

    private Task<(int ExitCode, IReadOnlyList<RunAllExecutor.CommandOutcome> Outcomes)> ExecuteBatchAsync()
    {
        var runner = new TaskRunner(TestConfig.Create(), _git, _agent, _processes, _project.Root, _ => { });
        return RunAllExecutor.ExecuteAsync(() => PromptScanner.Scan(_project.Root), runner.RunAsync, _ => { });
    }

    [Fact]
    public async Task Batch_RunsAllReadyCommandsInOrderThenStops()
    {
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a"));
        _project.WriteCmd("0002-b", TempProject.CmdMarkdown(branch: "task/0002-b"));
        _project.WriteCmd("0003-done", TempProject.CmdMarkdown(status: "done", branch: "task/0003-done"));

        var (exit, outcomes) = await ExecuteBatchAsync();

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["0001-a", "0002-b"], outcomes.Select(o => o.Id));
        Assert.All(outcomes, o => Assert.Equal(ExitCodes.Success, o.ExitCode));
    }

    [Fact]
    public async Task Batch_ContinuesPastAnAbortedCommandAndAggregatesExitCode()
    {
        // Command 0001 fails all its build attempts (3 by default); 0002 then succeeds on defaults.
        for (var i = 0; i < 3; i++)
            _processes.ShellResults.Enqueue(new ProcessResult(1, "build broken", "", false));
        _project.WriteCmd("0001-broken", TempProject.CmdMarkdown(branch: "task/0001-broken"));
        _project.WriteCmd("0002-good", TempProject.CmdMarkdown(branch: "task/0002-good"));

        var (exit, outcomes) = await ExecuteBatchAsync();

        Assert.Equal(ExitCodes.TaskFailed, exit);
        Assert.Equal([ExitCodes.TaskFailed, ExitCodes.Success], outcomes.Select(o => o.ExitCode));
    }

    [Fact]
    public async Task Batch_ReturnsSuccessOnEmptyQueue()
    {
        var (exit, outcomes) = await ExecuteBatchAsync();

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(outcomes);
    }

    [Fact]
    public async Task Batch_StopsOnSystemicProblemInsteadOfRefusingEveryCommand()
    {
        _git.Clean = false; // dirty tree refuses every command the same way
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a"));
        _project.WriteCmd("0002-b", TempProject.CmdMarkdown(branch: "task/0002-b"));

        var (exit, outcomes) = await ExecuteBatchAsync();

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Single(outcomes); // stopped after the first refusal
    }

    // --- Cross-command session policy (v0.1 FR-013): only within one invocation ---

    [Fact]
    public async Task ConsecutiveSameGroupCommands_ShareOneAgentSession()
    {
        _agent.Results.Enqueue(new AgentResult(0, "sess-1", "ok", false)); // 0001 plan
        _agent.Results.Enqueue(new AgentResult(0, "sess-1", "ok", false)); // 0001 implement
        _agent.Results.Enqueue(new AgentResult(0, "sess-1", "ok", false)); // 0002 plan (resumed)
        _agent.Results.Enqueue(new AgentResult(0, "sess-1", "ok", false)); // 0002 implement (resumed)
        _agent.Results.Enqueue(new AgentResult(0, "sess-3", "ok", false)); // 0003 plan (fresh, different group)
        _agent.Results.Enqueue(new AgentResult(0, "sess-3", "ok", false)); // 0003 implement
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a", group: "g1"));
        _project.WriteCmd("0002-b", TempProject.CmdMarkdown(branch: "task/0002-b", group: "g1"));
        _project.WriteCmd("0003-c", TempProject.CmdMarkdown(branch: "task/0003-c", group: "g2"));

        await ExecuteBatchAsync();

        Assert.Equal([null, "sess-1", "sess-1", "sess-1", null, "sess-3"], _agent.ResumeIds);
    }

    [Fact]
    public async Task FreshSessionFlag_ForcesANewSessionDespiteMatchingGroup()
    {
        _agent.Results.Enqueue(new AgentResult(0, "sess-1", "ok", false)); // 0001 plan
        _agent.Results.Enqueue(new AgentResult(0, "sess-1", "ok", false)); // 0001 implement
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a", group: "g1"));
        _project.WriteCmd("0002-b", TempProject.CmdMarkdown(branch: "task/0002-b", group: "g1", freshSession: true));

        await ExecuteBatchAsync();

        // 0001-a's own plan+implement calls (2) resume each other as usual; 0002-b starts fresh
        // despite matching group, since fresh_session: true overrides group continuity.
        Assert.Equal([null, "sess-1", null, "session-1"], _agent.ResumeIds);
    }

    [Fact]
    public async Task CommandsWithoutGroup_NeverShareSessions()
    {
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a"));
        _project.WriteCmd("0002-b", TempProject.CmdMarkdown(branch: "task/0002-b"));

        await ExecuteBatchAsync();

        // Each command's own plan+implement calls (2) resume each other as usual (within-command
        // continuity, v0.1 FR-012); neither command resumes the other's session (no group).
        Assert.Equal([null, "session-1", null, "session-3"], _agent.ResumeIds);
    }

    public void Dispose() => _project.Dispose();
}
