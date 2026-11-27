using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Execution;
using MagnaFlow.WorkerController.Infrastructure;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

/// <summary>Path-scoped build/test commands in the attempt loop (prompts/0037 decisions 3-5).</summary>
public class PathScopedRunTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly FakeGitClient _git = new();
    private readonly FakeAgentRunner _agent = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly List<string> _infos = [];

    private static readonly CheckCommand Backend = "backend build";
    private static readonly CheckCommand Topo = new("topo build", ["topo/"]);
    private static readonly CheckCommand BackendTest = new("backend test", ["klaas-backend/", "more/cmd/x.js"]);
    private static readonly CheckCommand TopoTest = new("topo test", ["topo/"]);

    private async Task<int> RunAsync(IReadOnlyList<CheckCommand> build, IReadOnlyList<CheckCommand> test, int maxAttempts = 3)
    {
        _project.WriteCmd("0001-test", TempProject.CmdMarkdown(branch: null, maxAttempts: maxAttempts));
        var scanned = PromptScanner.FindById(PromptScanner.Scan(_project.Root), "0001-test")!;
        var config = TestConfig.Create(maxAttempts: maxAttempts, buildCommands: build, testCommands: test);
        var runner = new TaskRunner(config, _git, _agent, _processes, _project.Root, _infos.Add, _ => { });
        return await runner.RunAsync(scanned, new AgentSessionState());
    }

    private string Log(string name) => File.ReadAllText(Path.Combine(_project.EvidenceDirectory("0001-test"), name));

    [Fact]
    public async Task An_untouched_scoped_command_is_skipped_logged_and_counts_as_passed()
    {
        _git.TouchedPaths = () => ["klaas-backend/a.cs"];

        var exit = await RunAsync([Backend, Topo], [BackendTest]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["backend build", "backend test"], _processes.ShellCommands);
        Assert.Contains("[0001-test] attempt 1/3: build 2/2 skipped (no change under topo/)", _infos);
        Assert.Contains("skipped build 2/2: topo build (no change under topo/)", Log("build.log"));
        Assert.Contains("touched paths: 1 (klaas-backend/ 1)", Log("build.log"));
        Assert.Contains("touched paths: 1 (klaas-backend/ 1)", Log("test.log"));
    }

    [Fact]
    public async Task A_mixed_list_runs_unscoped_and_touched_commands_in_order()
    {
        _git.TouchedPaths = () => ["topo/map.ts", "README.md"];

        var exit = await RunAsync([Backend, Topo], [BackendTest, TopoTest]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["backend build", "topo build", "topo test"], _processes.ShellCommands);
        Assert.Contains("[0001-test] attempt 1/3: test 1/2 skipped (no change under klaas-backend/, more/cmd/x.js)", _infos);
        Assert.Contains("touched paths: 2 (topo/ 1, (root) 1)", Log("build.log"));
    }

    [Fact]
    public async Task All_test_commands_skipped_passes_the_attempt_with_its_own_line()
    {
        _git.TouchedPaths = () => ["docs/notes.md"];

        var exit = await RunAsync([Backend], [BackendTest, TopoTest]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["backend build"], _processes.ShellCommands);
        Assert.Contains("[0001-test] attempt 1/3: tests: all skipped, no touched paths", _infos);
        Assert.DoesNotContain("[0001-test] attempt 1/3: tests", _infos);
        Assert.Contains("tests: all skipped, no touched paths", Log("test.log"));
        Assert.Contains("skipped test 2/2: topo test", Log("test.log"));
        // the agent's rules line still says the worker runs the tests: the gate exists
        Assert.Contains("build and tests will be run", _agent.Prompts[1]);
    }

    [Fact]
    public async Task A_touched_config_yml_runs_every_command()
    {
        _git.TouchedPaths = () => [".magnaflow/config.yml"];

        var exit = await RunAsync([Backend, Topo], [BackendTest, TopoTest]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["backend build", "topo build", "backend test", "topo test"], _processes.ShellCommands);
        Assert.Contains(".magnaflow/config.yml changed: every command runs", Log("build.log"));
    }

    [Fact]
    public async Task The_start_point_is_the_claim_commit_and_touched_paths_are_recomputed_per_attempt()
    {
        // Attempt 1 touches topo/ and its build fails; attempt 2's agent also touches the backend.
        // Prompts: [0] plan, [1] attempt 1, [2] attempt 2 — so 3 prompts = attempt 2's agent has run.
        _git.TouchedPaths = () => _agent.Prompts.Count >= 3 ? ["topo/a.ts", "klaas-backend/b.cs"] : ["topo/a.ts"];
        _processes.ShellResults.Enqueue(new ProcessResult(0, "", "", false));          // attempt 1: backend build
        _processes.ShellResults.Enqueue(new ProcessResult(1, "topo broke", "", false)); // attempt 1: topo build fails

        var exit = await RunAsync([Backend, Topo], [BackendTest]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["backend build", "topo build", "backend build", "topo build", "backend test"], _processes.ShellCommands);
        Assert.All(_git.TouchedPathStartPoints, p => Assert.Equal("c1", p)); // c1 = the claim commit
        Assert.Contains("currently will run", _agent.Prompts[2]); // the retry feedback carries the refreshed list
    }

    [Fact]
    public async Task Without_paths_git_is_never_asked_and_nothing_extra_is_logged()
    {
        var exit = await RunAsync(["build-cmd"], ["test-cmd"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_git.TouchedPathStartPoints);
        Assert.DoesNotContain("touched paths", Log("build.log"));
        Assert.DoesNotContain("# Checks", _agent.Prompts[0]);
    }

    [Fact]
    public async Task The_prompts_list_per_check_whether_it_will_run()
    {
        _git.TouchedPaths = () => [];

        await RunAsync([Backend, Topo], [BackendTest]);

        foreach (var prompt in new[] { _agent.Prompts[0], _agent.Prompts[1] }) // plan and implementation
        {
            Assert.Contains("# Checks after each attempt", prompt);
            Assert.Contains("- build 1/2 `backend build`: always runs", prompt);
            Assert.Contains("- build 2/2 `topo build`: runs only when your changes touch topo/ — currently will be skipped", prompt);
            Assert.Contains("- test 1/1 `backend test`: runs only when your changes touch klaas-backend/, more/cmd/x.js", prompt);
        }
    }

    public void Dispose() => _project.Dispose();
}
