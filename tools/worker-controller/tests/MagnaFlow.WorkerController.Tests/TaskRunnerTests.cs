using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Execution;
using MagnaFlow.WorkerController.Infrastructure;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class TaskRunnerTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly FakeGitClient _git = new();
    private readonly FakeAgentRunner _agent = new();
    private readonly FakeProcessRunner _processes = new();

    private readonly List<string> _errors = [];

    private TaskRunner CreateRunner(int maxAttempts = 3) =>
        CreateRunner(TestConfig.Create(maxAttempts));

    private TaskRunner CreateRunner(ProjectConfig config) =>
        new(config, _git, _agent, _processes, _project.Root, _ => { }, _errors.Add);

    private ScannedCommand WriteAndScan(string content, string id = "0001-test")
    {
        _project.WriteCmd(id, content);
        return PromptScanner.FindById(PromptScanner.Scan(_project.Root), id)!;
    }

    // --- Refusals mutate nothing ---

    [Fact]
    public async Task NonReadyCommand_IsRefusedWithoutAnyMutation()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(status: "done"));
        var before = File.ReadAllText(scanned.Cmd!.FilePath);

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Equal(before, File.ReadAllText(scanned.Cmd.FilePath));
        Assert.Empty(_git.MutatingOperations);
    }

    [Fact]
    public async Task StuckRunningCommand_IsRefused()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(status: "running"));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Empty(_git.MutatingOperations);
    }

    [Fact]
    public async Task StillInQuestionsStatus_RefusesToRunDirectly()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(status: "questions"));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Empty(_git.MutatingOperations);
        Assert.Empty(_agent.Prompts);
    }

    [Fact]
    public async Task DirtyWorkingTree_IsRefusedWithoutAnyMutation()
    {
        _git.Clean = false;
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Empty(_git.MutatingOperations);
    }

    [Fact]
    public async Task MissingSpecFile_IsUsageErrorWithoutAnyMutation()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(specs: ["docs/does-not-exist.md"]));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.UsageError, exit);
        Assert.Empty(_git.MutatingOperations);
        Assert.Empty(_agent.Prompts);
    }

    [Fact]
    public async Task UnavailableAgent_IsEnvironmentError()
    {
        _agent.Available = false;
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.EnvironmentError, exit);
        Assert.Empty(_git.MutatingOperations);
    }

    [Fact]
    public async Task UnavailableGit_IsEnvironmentError()
    {
        _git.Available = false;
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        Assert.Equal(ExitCodes.EnvironmentError, await CreateRunner().RunAsync(scanned, new AgentSessionState()));
    }

    [Fact]
    public async Task MissingBaseBranch_IsUsageErrorWithoutAnyMutation()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(baseBranch: "does-not-exist"));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.UsageError, exit);
        Assert.Empty(_git.MutatingOperations);
    }

    [Fact]
    public async Task InvokingFromACommandWorkBranch_IsRefusedWithoutMutation()
    {
        // Command 0002 owns branch task/0002-other; the user is standing on it.
        WriteAndScan(TempProject.CmdMarkdown(branch: "task/0002-other"), "0002-other");
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));
        _git.CurrentBranch = "task/0002-other";
        _git.Branches.Add("task/0002-other");
        var before = File.ReadAllText(scanned.Cmd!.FilePath);

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Equal(before, File.ReadAllText(scanned.Cmd.FilePath));
        Assert.Empty(_git.MutatingOperations);
        Assert.Empty(_agent.Prompts);
    }

    [Fact]
    public async Task BranchlessCommand_RunsEntirelyOnTheInvokingBranch()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([
            "stage docs/prompts/0001-cmd-test.md",
            "commit mf-worker: 0001-test -> running",
            "stage-all-except-bookkeeping",
            "stage docs/prompts/0001-cmd-test.md",
            "stage docs/prompts/0001-rst-test.md",
            "stage .magnaflow/0001-test",
            "amend command 0001-test: Test command",
        ], _git.Operations); // no create, no checkout — everything on the invoking branch
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, cmd!.Status);
    }

    // --- The fold: a branchless run lands as one commit unless amending would be unsafe ---

    [Fact]
    public async Task BranchlessRun_LandsAsOneCommitCarryingBothTheStatusTransitionAndTheWork()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        var commit = Assert.Single(_git.Commits);
        Assert.Equal("command 0001-test: Test command", commit.Message);
        Assert.Contains(FakeGitClient.WorkChanges, commit.Paths);                 // the change
        Assert.Contains("docs/prompts/0001-cmd-test.md", commit.Paths);           // pending -> done
        Assert.Contains("docs/prompts/0001-rst-test.md", commit.Paths);
        Assert.Contains(".magnaflow/0001-test", commit.Paths);
    }

    [Fact]
    public async Task BranchlessRunThatFails_FoldsTheSameWay()
    {
        _processes.ShellResults.Enqueue(new ProcessResult(1, "", "boom", TimedOut: false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null, maxAttempts: 1));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.TaskFailed, exit);
        var commit = Assert.Single(_git.Commits);
        Assert.Equal("command 0001-test: Test command", commit.Message);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Aborted, cmd!.Status);
    }

    [Fact]
    public async Task AgentCommittedOnItsOwn_FallsBackToTwoCommits()
    {
        _agent.OnNextRun = _git.CommitBehindTheWorkersBack; // HEAD is no longer the claim commit
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("amend"));
        Assert.Equal([
            "mf-worker: 0001-test -> running",
            "command 0001-test: Test command",
            "mf-worker: 0001-test -> done (1 attempt(s))",
        ], _git.Commits.Select(c => c.Message));
    }

    [Fact]
    public async Task ClaimCommitAlreadyOnARemoteTrackingRef_FallsBackToTwoCommits()
    {
        _git.CommitsAreOnARemoteTrackingRef = true; // amending it would mean a force-push
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("amend"));
        Assert.Equal([
            "mf-worker: 0001-test -> running",
            "command 0001-test: Test command",
            "mf-worker: 0001-test -> done (1 attempt(s))",
        ], _git.Commits.Select(c => c.Message));
    }

    [Fact]
    public async Task BranchlessRunThatNeverReachesTheWorkPlane_KeepsItsOwnStatusCommit()
    {
        _agent.Results.Enqueue(new AgentResult(1, null, null, TimedOut: false));
        _agent.Results.Enqueue(new AgentResult(1, null, null, TimedOut: false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null, maxAttempts: 2));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        // Planning never gated through, so there is no work to fold and nothing to gain by
        // rewriting the claim commit under a message that claims work was done.
        Assert.Equal(ExitCodes.TaskFailed, exit);
        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("amend"));
        Assert.Equal([
            "mf-worker: 0001-test -> running",
            "mf-worker: 0001-test -> aborted (2 attempt(s))",
        ], _git.Commits.Select(c => c.Message));
    }

    [Fact]
    public async Task BranchMode_KeepsTheSplitAndNeverAmends()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("amend"));
        var work = Assert.Single(_git.Commits, c => c.Message == "command 0001-test: Test command");
        Assert.Equal([FakeGitClient.WorkChanges], work.Paths); // code only, no bookkeeping folded in
        Assert.Contains(_git.Commits, c => c.Message == "mf-worker: 0001-test -> done (1 attempt(s))");
    }

    [Fact]
    public async Task MalformedCommand_IsUsageError()
    {
        _project.WriteCmd("0001-broken", "no frontmatter");
        var scanned = PromptScanner.FindById(PromptScanner.Scan(_project.Root), "0001-broken")!;

        Assert.Equal(ExitCodes.UsageError, await CreateRunner().RunAsync(scanned, new AgentSessionState()));
    }

    // --- Happy path: the two-plane choreography, work branch created only after planning gates through ---

    [Fact]
    public async Task HappyPath_FollowsTheTwoPlaneCommitChoreography()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([
            "stage docs/prompts/0001-cmd-test.md",
            "commit mf-worker: 0001-test -> running",
            "create task/0001-test from main",
            "stage-all-except-bookkeeping",
            "commit command 0001-test: Test command",
            "checkout main",
            "stage docs/prompts/0001-cmd-test.md",
            "stage docs/prompts/0001-rst-test.md",
            "stage .magnaflow/0001-test",
            "commit mf-worker: 0001-test -> done (1 attempt(s))",
        ], _git.Operations);
    }

    // --- Remote sync: with a remote configured, both planes are pushed ---

    [Fact]
    public async Task WithRemote_PushesWorkBranchAndInvokingBranch()
    {
        _git.Remote = "origin";
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([
            "stage docs/prompts/0001-cmd-test.md",
            "commit mf-worker: 0001-test -> running",
            "create task/0001-test from main",
            "stage-all-except-bookkeeping",
            "commit command 0001-test: Test command",
            "push origin task/0001-test",
            "checkout main",
            "stage docs/prompts/0001-cmd-test.md",
            "stage docs/prompts/0001-rst-test.md",
            "stage .magnaflow/0001-test",
            "commit mf-worker: 0001-test -> done (1 attempt(s))",
            "push origin main",
        ], _git.Operations);
    }

    [Fact]
    public async Task WithRemote_BranchlessCommand_PushesInvokingBranchOnce()
    {
        _git.Remote = "origin";
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["push origin main"], _git.Operations.Where(o => o.StartsWith("push")));
    }

    [Fact]
    public async Task WithoutRemote_NothingIsPushed()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("push"));
    }

    [Fact]
    public async Task FailingPush_IsAWarningNotAFailedCommand()
    {
        _git.Remote = "origin";
        _git.PushFails = true;
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, cmd!.Status);
    }

    [Fact]
    public async Task HappyPath_WritesTerminalStateEvidenceAndReport()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, cmd!.Status);
        Assert.Equal(1, cmd.Attempts);

        var evidenceDir = _project.EvidenceDirectory("0001-test");
        Assert.True(File.Exists(Path.Combine(evidenceDir, "claude.log")));
        Assert.True(File.Exists(Path.Combine(evidenceDir, "build.log")));
        Assert.True(File.Exists(Path.Combine(evidenceDir, "test.log")));
        Assert.True(File.Exists(SessionEvidence.SessionPath(evidenceDir)));
        Assert.True(RstFile.Exists(cmd.RstPath));
        Assert.Contains("Command completed after 1 attempt(s)", File.ReadAllText(cmd.RstPath));
    }

    [Fact]
    public async Task ExistingWorkBranch_IsReusedInsteadOfCreated()
    {
        _git.Branches.Add("task/0001-test");
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Contains("checkout task/0001-test", _git.Operations);
        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("create "));
    }

    // --- Retry loop (v0.1 FR-010), now with a plan-phase agent call ahead of it ---

    [Fact]
    public async Task FailingBuild_RetriesWithFeedbackUntilMaxAttemptsThenAborts()
    {
        _processes.ShellResults.Enqueue(new ProcessResult(1, "CS0103: name does not exist", "", false)); // build 1
        _processes.ShellResults.Enqueue(new ProcessResult(1, "CS0103: still broken", "", false));   // build 2
        var scanned = WriteAndScan(TempProject.CmdMarkdown(maxAttempts: 2));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.TaskFailed, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Aborted, cmd!.Status);
        Assert.Equal(2, cmd.Attempts);
        Assert.Equal(3, _agent.Prompts.Count); // plan + 2 implementation attempts
        Assert.Contains("CS0103: name does not exist", _agent.Prompts[2]); // feedback prompt wraps failure output
        Assert.Contains("build kept failing", File.ReadAllText(cmd.RstPath));
    }

    [Fact]
    public async Task FailingTestsThenSuccess_EndsDoneWithTwoAttempts()
    {
        _processes.ShellResults.Enqueue(new ProcessResult(0, "build ok", "", false));      // build 1
        _processes.ShellResults.Enqueue(new ProcessResult(1, "1 test failed", "", false)); // test 1
        // attempt 2: defaults (success) for build and test
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, cmd!.Status);
        Assert.Equal(2, cmd.Attempts);
        Assert.Contains("1 test failed", _agent.Prompts[2]);
    }

    [Fact]
    public async Task CrashingAgentDuringPlanning_CountsAsFailedAttempt()
    {
        _agent.Results.Enqueue(new AgentResult(1, null, null, TimedOut: false));  // plan call crashes
        _agent.Results.Enqueue(new AgentResult(0, "s2", "planned", TimedOut: false)); // retry succeeds
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(2, cmd!.Attempts); // 1 for the plan crash, 1 for the (auto-succeeding) implementation attempt
    }

    [Fact]
    public async Task CrashingAgentDuringExecution_CountsAsFailedAttempt()
    {
        _agent.Results.Enqueue(new AgentResult(0, "s-plan", "planned", TimedOut: false)); // plan succeeds
        _agent.Results.Enqueue(new AgentResult(1, null, null, TimedOut: false));           // implementation crashes
        _agent.Results.Enqueue(new AgentResult(0, "s2", "fixed", TimedOut: false));        // retry succeeds
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(2, cmd!.Attempts);
    }

    [Fact]
    public async Task PlanningCrashesRepeatedly_AbortsWithoutEverCreatingTheWorkBranch()
    {
        _agent.Results.Enqueue(new AgentResult(1, null, null, TimedOut: false));
        _agent.Results.Enqueue(new AgentResult(1, null, null, TimedOut: false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test", maxAttempts: 2));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.TaskFailed, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Aborted, cmd!.Status);
        Assert.Equal(2, cmd.Attempts);
        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("create"));
        Assert.Contains("planning failed", File.ReadAllText(cmd.RstPath));
    }

    [Fact]
    public async Task LogFilesAreFullyFlushedToDiskBeforeTheTerminalCommitStagesThem()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());
        var evidenceDir = _project.EvidenceDirectory("0001-test");
        var stagedLogSizes = new List<long>();
        _git.StageObserver = _ =>
        {
            var log = Path.Combine(evidenceDir, "claude.log");
            stagedLogSizes.Add(File.Exists(log) ? new FileInfo(log).Length : -1);
        };

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        // First stage = running commit (log doesn't exist yet); last stage = terminal commit:
        // by then the log MUST be non-empty on disk, or git commits empty files (FileStream buffering).
        Assert.Equal(-1, stagedLogSizes.First());
        Assert.True(stagedLogSizes.Last() > 0, "claude.log was still empty on disk when the terminal commit staged it");
    }

    // --- Plan phase and the pause/resume gate (spec FR-008-FR-015) ---

    [Fact]
    public async Task PlanRaisesOpenQuestions_PausesWithoutBuildOrTest()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());
        PlnFile.Write(scanned.Cmd!.PlnPath, "Initial plan.", openQuestions: ["Which environment should this target?"]);

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit); // a legitimate pause is not a failure
        var (cmd, _) = CmdFile.Parse(scanned.Cmd.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Questions, cmd!.Status);
        Assert.Equal(0, cmd.Attempts); // a clean pause never increments attempts
        Assert.True(QaFile.HasUnansweredQuestion(cmd.QaPath));
        Assert.Contains("Which environment should this target?", File.ReadAllText(cmd.QaPath));
        Assert.Empty(_processes.ShellCommands); // no build/test ran
        Assert.False(RstFile.Exists(cmd.RstPath)); // no report for a paused run
    }

    [Fact]
    public async Task PlanPause_WithRemote_PushesTheQuestionsBookkeeping()
    {
        _git.Remote = "origin";
        var scanned = WriteAndScan(TempProject.CmdMarkdown());
        PlnFile.Write(scanned.Cmd!.PlnPath, "plan", openQuestions: ["Q?"]);

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(["push origin main"], _git.Operations.Where(o => o.StartsWith("push")));
    }

    [Fact]
    public async Task PlanPause_NeverTouchesTheWorkBranch()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));
        PlnFile.Write(scanned.Cmd!.PlnPath, "plan", openQuestions: ["Q?"]);

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("create") || o.StartsWith("checkout"));
    }

    [Fact]
    public async Task ResumingAfterAnswer_UsesSelfResumeSessionWithNoExplicitField()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());
        PlnFile.Write(scanned.Cmd!.PlnPath, "plan", openQuestions: ["Q?"]);
        _agent.Results.Enqueue(new AgentResult(0, "sess-paused", "asked", false));
        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        // The human answers and resets status to ready; the re-plan resolves (no more open questions).
        var (pausedCmd, _) = CmdFile.Parse(scanned.Cmd.FilePath, scanned.Id);
        pausedCmd!.WriteStatus(CmdStatus.Ready);
        File.WriteAllText(pausedCmd.PlnPath, "# Plan\n\nResolved after the human's answer.\n");
        _agent.Prompts.Clear();
        _agent.ResumeIds.Clear();

        var rescanned = PromptScanner.FindById(PromptScanner.Scan(_project.Root), scanned.Id)!;
        var exit = await CreateRunner().RunAsync(rescanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("sess-paused", _agent.ResumeIds[0]); // self-resume: no context repeated
        var (finalCmd, _) = CmdFile.Parse(scanned.Cmd.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, finalCmd!.Status);
    }

    [Fact]
    public async Task GroupContinuity_TakesPrecedenceOverSelfResume()
    {
        _project.WriteSessionEvidence("0001-test", "sess-from-evidence");
        var scanned = WriteAndScan(TempProject.CmdMarkdown(group: "g1"));
        var session = new AgentSessionState { SessionId = "sess-of-group", Group = "g1" };

        await CreateRunner().RunAsync(scanned, session);

        Assert.Equal("sess-of-group", _agent.ResumeIds[0]);
    }

    [Fact]
    public async Task SecondPauseRound_AppendsToTheSameQaFile()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());
        PlnFile.Write(scanned.Cmd!.PlnPath, "plan v1", openQuestions: ["First question?"]);
        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        var (pausedCmd, _) = CmdFile.Parse(scanned.Cmd.FilePath, scanned.Id);
        pausedCmd!.WriteStatus(CmdStatus.Ready);
        PlnFile.Write(pausedCmd.PlnPath, "plan v2", openQuestions: ["Second question?"]);

        var rescanned = PromptScanner.FindById(PromptScanner.Scan(_project.Root), scanned.Id)!;
        await CreateRunner().RunAsync(rescanned, new AgentSessionState());

        var qaText = File.ReadAllText(pausedCmd.QaPath);
        Assert.Contains("## Question (round 1)", qaText);
        Assert.Contains("## Question (round 2)", qaText);
        Assert.Contains("First question?", qaText);
        Assert.Contains("Second question?", qaText);
    }

    // --- Multiple build/test commands (contracts/file-formats.md `commands:`) ---

    [Fact]
    public async Task MultipleBuildCommands_AllRunInOrderOnSuccess()
    {
        var config = TestConfig.Create(buildCommands: ["dotnet build a", "dotnet build b", "npm run build"]);
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner(config).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["dotnet build a", "dotnet build b", "npm run build", "test-cmd"], _processes.ShellCommands);
    }

    [Fact]
    public async Task MultipleBuildCommands_StopsAtFirstFailureAndUsesItsOutputAsFeedback()
    {
        _processes.ShellResults.Enqueue(new ProcessResult(0, "a ok", "", false));               // attempt 1, command 1
        _processes.ShellResults.Enqueue(new ProcessResult(1, "b broken", "b stderr", false));    // attempt 1, command 2 fails
        _processes.ShellResults.Enqueue(new ProcessResult(0, "a ok", "", false));               // attempt 2, command 1
        _processes.ShellResults.Enqueue(new ProcessResult(1, "b broken", "b stderr", false));    // attempt 2, command 2 fails
        var config = TestConfig.Create(maxAttempts: 2, buildCommands: ["dotnet build a", "dotnet build b", "npm run build"]);
        var scanned = WriteAndScan(TempProject.CmdMarkdown(maxAttempts: 2));

        var exit = await CreateRunner(config).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.TaskFailed, exit);
        // command 3 ("npm run build") never runs in either attempt — the sequence stops at the first failure
        Assert.Equal(["dotnet build a", "dotnet build b", "dotnet build a", "dotnet build b"], _processes.ShellCommands);
        Assert.Contains("b broken", _agent.Prompts[2]); // attempt 2's prompt carries attempt 1's failing command output
        Assert.Contains("b stderr", _agent.Prompts[2]);
    }

    [Fact]
    public async Task MultipleTestCommands_RunSequentiallyAfterBuildSucceeds()
    {
        var config = TestConfig.Create(testCommands: ["dotnet test a", "dotnet test b"]);
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner(config).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["build-cmd", "dotnet test a", "dotnet test b"], _processes.ShellCommands);
    }

    // --- Report (spec FR-017-FR-019) ---

    [Fact]
    public async Task AbortedRun_RstRecordsTheSpecificReason()
    {
        for (var i = 0; i < 3; i++)
            _processes.ShellResults.Enqueue(new ProcessResult(1, "boom", "", false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown(maxAttempts: 1));

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Contains("build kept failing", File.ReadAllText(scanned.Cmd!.RstPath));
    }

    // --- Session policy within a command (v0.1 FR-012) ---

    [Fact]
    public async Task RetryWithinCommand_ResumesTheSameAgentSession()
    {
        _agent.Results.Enqueue(new AgentResult(0, "sess-A", "planned", TimedOut: false));       // plan
        _agent.Results.Enqueue(new AgentResult(0, "sess-A", "first attempt", TimedOut: false)); // implementation attempt 1
        _processes.ShellResults.Enqueue(new ProcessResult(1, "build broken", "", false));       // force retry
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal([null, "sess-A", "sess-A"], _agent.ResumeIds);
    }

    [Fact]
    public async Task ResumeWithRawSessionId_StartsTheAgentInThatSession()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(resume: "6a1f0e6e-1234-4bcd-9ef0-abcdef012345"));

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal("6a1f0e6e-1234-4bcd-9ef0-abcdef012345", _agent.ResumeIds[0]);
    }

    [Fact]
    public async Task ResumeWithCommandReference_ContinuesThatCommandsRecordedSession()
    {
        _project.WriteSessionEvidence("0001-earlier", "sess-of-0001");
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0002-test", resume: "0001-earlier"), "0002-test");

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("sess-of-0001", _agent.ResumeIds[0]);
    }

    [Fact]
    public async Task ResumeReferencingACommandThatNeverRan_IsUsageErrorWithoutMutation()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(resume: "0009-does-not-exist"));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.UsageError, exit);
        Assert.Empty(_git.MutatingOperations);
        Assert.Empty(_agent.Prompts);
    }

    [Fact]
    public async Task ExplicitResume_TakesPrecedenceOverGroupContinuity()
    {
        _project.WriteSessionEvidence("0001-earlier", "sess-expliciet");
        var scanned = WriteAndScan(TempProject.CmdMarkdown(group: "g1", resume: "0001-earlier"));
        var session = new AgentSessionState { SessionId = "sess-of-group", Group = "g1" };

        await CreateRunner().RunAsync(scanned, session);

        Assert.Equal("sess-expliciet", _agent.ResumeIds[0]);
    }

    [Fact]
    public async Task CompletedCommand_RecordsItsFinalSessionAsEvidence()
    {
        _agent.Results.Enqueue(new AgentResult(0, "sess-final", "planned", TimedOut: false));
        _agent.Results.Enqueue(new AgentResult(0, "sess-final", "finished", TimedOut: false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal("sess-final", SessionEvidence.ReadSessionId(_project.EvidenceDirectory("0001-test")));
    }

    [Fact]
    public async Task CompletedCommand_HandsItsSessionAndGroupToTheSessionState()
    {
        _agent.Results.Enqueue(new AgentResult(0, "sess-X", "planned", TimedOut: false));
        _agent.Results.Enqueue(new AgentResult(0, "sess-X", "finished", TimedOut: false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown(group: "001-feature"));
        var session = new AgentSessionState();

        await CreateRunner().RunAsync(scanned, session);

        Assert.Equal("sess-X", session.SessionId);
        Assert.Equal("001-feature", session.Group);
    }

    // --- mf-run stop/start integration (docs/decisions/0010-mf-run-design.md "Worker integration") ---

    [Fact]
    public async Task NoRunServicesConfigured_NeverInvokesMfRun()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: false)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_processes.ExecutableCalls);
    }

    [Fact]
    public async Task RunServicesConfigured_StopsBeforeImplementationAndStartsAfterDone()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: true)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["mf-run", "mf-run"], _processes.ExecutableCalls.Select(c => c.Executable));
        Assert.Equal(["stop", "--project", _project.Root], _processes.ExecutableCalls[0].Arguments);
        Assert.Equal(["start", "--project", _project.Root], _processes.ExecutableCalls[1].Arguments);
    }

    [Fact]
    public async Task RunCommandIsSubstitutableLikeAgentCommand()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        await CreateRunner(TestConfig.Create(hasRunServices: true, runCommand: @"C:\stubs\mf-run-stub.exe"))
            .RunAsync(scanned, new AgentSessionState());

        Assert.All(_processes.ExecutableCalls, c => Assert.Equal(@"C:\stubs\mf-run-stub.exe", c.Executable));
    }

    [Fact]
    public async Task StopFailure_RefusesImplementationRevertsToReadyAndConsumesNoAttempt()
    {
        _processes.ExecutableResults.Enqueue(new ProcessResult(1, "", "port in use", false)); // stop fails
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: true)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Ready, cmd!.Status);
        Assert.Equal(0, cmd.Attempts);
        Assert.Single(_processes.ExecutableCalls); // stop only — start never fires on a refusal
        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("create") || o.StartsWith("checkout"));
        Assert.Equal([
            "stage docs/prompts/0001-cmd-test.md",
            "commit mf-worker: 0001-test -> running",
            "stage docs/prompts/0001-cmd-test.md",
            "stage .magnaflow/0001-test",
            "commit mf-worker: 0001-test -> ready (mf-run stop failed)",
        ], _git.Operations);
    }

    [Fact]
    public async Task StopSpawnFailure_IsTreatedTheSameAsNonZeroExit()
    {
        _processes.NextExecutableThrows = new InvalidOperationException("mf-run not found on PATH");
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: true)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Ready, cmd!.Status);
    }

    [Fact]
    public async Task StartFailure_AppendsWarningToRstWithoutChangingStatusOrExitCode()
    {
        _processes.ExecutableResults.Enqueue(new ProcessResult(0, "", "", false)); // stop succeeds
        _processes.ExecutableResults.Enqueue(new ProcessResult(1, "web: exited shortly after starting", "", false)); // start fails
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: true)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit); // a start failure never changes the run's own outcome
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, cmd!.Status);
        var rst = File.ReadAllText(cmd.RstPath);
        Assert.Contains("## Warning", rst);
        Assert.Contains("web: exited shortly after starting", rst);
    }

    [Fact]
    public async Task StartFiresAfterAbortedFromFailingBuildToo()
    {
        for (var i = 0; i < 3; i++)
            _processes.ShellResults.Enqueue(new ProcessResult(1, "boom", "", false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown(maxAttempts: 1));

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: true)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.TaskFailed, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Aborted, cmd!.Status);
        Assert.Equal(["stop", "start"], _processes.ExecutableCalls.Select(c => c.Arguments[0]));
    }

    [Fact]
    public async Task StartFiresAfterAbortedEvenWhenPlanningNeverGatedThrough()
    {
        _agent.Results.Enqueue(new AgentResult(1, null, null, TimedOut: false)); // plan crashes
        var scanned = WriteAndScan(TempProject.CmdMarkdown(maxAttempts: 1));

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: true)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.TaskFailed, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Aborted, cmd!.Status);
        // stop never fires — planGatePassed was never reached — but start still does (design: "done and aborted both")
        Assert.Equal(["start"], _processes.ExecutableCalls.Select(c => c.Arguments[0]));
    }

    [Fact]
    public async Task QuestionsPause_NeitherStopsNorStarts()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown());
        PlnFile.Write(scanned.Cmd!.PlnPath, "plan", openQuestions: ["Q?"]);

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: true)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Questions, cmd!.Status);
        Assert.Empty(_processes.ExecutableCalls); // stopped nothing, so it starts nothing
    }

    // --- Evidence staging must never cost a run its commit (0017) ---
    //
    // `git add -- .magnaflow/<id>` fails outright when the folder was never written, and again
    // when the project ignores .magnaflow/. Both used to throw out of the terminal block, past
    // the commit and the push, leaving a finished run recorded nowhere.

    private const string EvidencePath = ".magnaflow/0001-test";

    [Theory]
    [InlineData(StageSkipReason.NotOnDisk)]
    [InlineData(StageSkipReason.IgnoredByTheProject)]
    public async Task BranchlessRun_WhoseEvidenceCannotBeStaged_StillLandsAndPushesItsOneCommit(StageSkipReason reason)
    {
        _git.Remote = "origin";
        _git.UnstageablePaths[EvidencePath] = reason;
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        var commit = Assert.Single(_git.Commits);
        Assert.Equal("command 0001-test: Test command", commit.Message);
        Assert.Contains(FakeGitClient.WorkChanges, commit.Paths);        // the change
        Assert.Contains("docs/prompts/0001-cmd-test.md", commit.Paths);  // ready -> done
        Assert.Contains("docs/prompts/0001-rst-test.md", commit.Paths);
        Assert.DoesNotContain(EvidencePath, commit.Paths);               // the one thing given up
        Assert.Equal(["push origin main"], _git.Operations.Where(o => o.StartsWith("push")));
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, cmd!.Status);
    }

    [Theory]
    [InlineData(StageSkipReason.NotOnDisk)]
    [InlineData(StageSkipReason.IgnoredByTheProject)]
    public async Task PlanPause_WhoseEvidenceCannotBeStaged_StillCommitsTheQuestionsTransition(StageSkipReason reason)
    {
        _git.UnstageablePaths[EvidencePath] = reason;
        var scanned = WriteAndScan(TempProject.CmdMarkdown());
        PlnFile.Write(scanned.Cmd!.PlnPath, "plan", openQuestions: ["Q?"]);

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        var commit = Assert.Single(_git.Commits, c => c.Message == "mf-worker: 0001-test -> questions");
        Assert.Contains("docs/prompts/0001-qa-test.md", commit.Paths);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Questions, cmd!.Status);
    }

    [Theory]
    [InlineData(StageSkipReason.NotOnDisk)]
    [InlineData(StageSkipReason.IgnoredByTheProject)]
    public async Task StopRefusal_WhoseEvidenceCannotBeStaged_StillCommitsTheRevertToReady(StageSkipReason reason)
    {
        _git.UnstageablePaths[EvidencePath] = reason;
        _processes.ExecutableResults.Enqueue(new ProcessResult(1, "", "port in use", false)); // stop fails
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: "task/0001-test"));

        var exit = await CreateRunner(TestConfig.Create(hasRunServices: true)).RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.PreconditionRefused, exit);
        Assert.Contains(_git.Commits, c => c.Message == "mf-worker: 0001-test -> ready (mf-run stop failed)");
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Ready, cmd!.Status);
    }

    [Fact]
    public async Task EvidenceTheProjectDoesTrack_IsStillStagedIntoTheTerminalCommit()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains($"stage {EvidencePath}", _git.Operations);
        Assert.Contains(EvidencePath, Assert.Single(_git.Commits).Paths);
    }

    [Fact]
    public async Task TerminalCommitThatFailsAnyway_SaysWhatWasLostOnStderr()
    {
        // The rst is the command's own content, not evidence: a repo that cannot stage it is
        // genuinely broken, and the run must say so rather than exit on a grey one-liner.
        _git.StageObserver = path =>
        {
            if (path.EndsWith("0001-rst-test.md"))
                throw new GitException("git add failed (exit 128): index.lock exists");
        };
        var scanned = WriteAndScan(TempProject.CmdMarkdown(branch: null));

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.EnvironmentError, exit);
        var report = string.Join("\n", _errors);
        Assert.Contains("0001-test", report);
        Assert.Contains("commit FAILED", report);
        Assert.Contains("index.lock exists", report);
        Assert.Contains("0001-rst-test.md", report);
        Assert.Contains("dirty working tree", report);
        Assert.DoesNotContain(_git.Operations, o => o.StartsWith("push"));
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, cmd!.Status); // written, but uncommitted — exactly what it warns about
    }

    // --- Re-ready after abort: a fresh attempt budget (command-lifecycle.md) ---

    [Fact]
    public async Task ReReadiedExhaustedCommand_RunsTheAgentAgainWithAFreshBudget()
    {
        var scanned = WriteAndScan(TempProject.CmdMarkdown(attempts: 3, maxAttempts: 3));
        _project.WriteRst("0001-test", "---\ntitle: old\n---\n\n# Report\n\n## What was done\n\nOLD REPORT\n");

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, _agent.Prompts.Count); // plan + implementation, instead of an instant abort
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(CmdStatus.Done, cmd!.Status);
        Assert.Equal(1, cmd.Attempts);
        Assert.Contains("re-run after an earlier `aborted`", _agent.Prompts[1]);

        // The old rst is gone in the claim commit; the fake agent wrote none, so the fallback is fresh.
        Assert.Contains("docs/prompts/0001-rst-test.md", _git.Commits[0].Paths);
        var rst = File.ReadAllText(cmd.RstPath);
        Assert.DoesNotContain("OLD REPORT", rst);
        var whatWasDone = rst[(rst.IndexOf("## What was done", StringComparison.Ordinal) + "## What was done".Length)..].TrimStart();
        Assert.StartsWith("Re-run after an earlier `aborted`", whatWasDone);
    }

    [Fact]
    public async Task ResumeAfterQuestions_KeepsCountingAttempts()
    {
        _processes.ShellResults.Enqueue(new ProcessResult(1, "build broken", "", false));
        _processes.ShellResults.Enqueue(new ProcessResult(1, "build broken", "", false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown(attempts: 1, maxAttempts: 3));
        _project.WriteRst("0001-test", "---\ntitle: kept\n---\n\nKEPT\n");

        var exit = await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal(ExitCodes.TaskFailed, exit);
        var (cmd, _) = CmdFile.Parse(scanned.Cmd!.FilePath, scanned.Id);
        Assert.Equal(3, cmd!.Attempts); // 1 carried over + 2 failing builds; no reset
        Assert.Equal(3, _agent.Prompts.Count); // plan + 2 implementation attempts
        Assert.DoesNotContain(_agent.Prompts, p => p.Contains("This is a re-run"));
        Assert.Contains("KEPT", File.ReadAllText(cmd.RstPath));
    }

    [Fact]
    public void AbortReason_WhenNoPhaseRan_SaysSoInsteadOfAnEmptyPhase()
    {
        var reason = TaskRunner.AbortReason(planGatePassed: false, attemptsUsed: 3, maxAttempts: 3, lastFailingPhase: null);

        Assert.Contains("no phase ran", reason);
        Assert.DoesNotContain("phase: ", reason);
        Assert.EndsWith("plan", TaskRunner.AbortReason(false, 2, 2, "plan"));
        Assert.Equal("build kept failing after 2 attempt(s)", TaskRunner.AbortReason(true, 2, 2, "build"));
    }

    // --- Retry without a resumable session resends the command (worker-run.md) ---

    [Fact]
    public async Task PlanCrashWithoutSession_RetryCarriesTheCommandBody()
    {
        _agent.Results.Enqueue(new AgentResult(1, null, null, TimedOut: false)); // plan crashes, no session id
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Null(_agent.ResumeIds[1]);
        var retry = _agent.Prompts[1];
        Assert.Contains("Do something useful.", retry);            // the command body
        Assert.Contains("Before making any change, plan.", retry); // the full plan prompt
        Assert.Contains("failed while planning", retry);          // plus the plan-worded feedback
        Assert.DoesNotContain("after your changes", retry);
    }

    [Fact]
    public async Task PlanCrashWithASession_RetrySendsOnlyTheFeedback()
    {
        _agent.Results.Enqueue(new AgentResult(1, "s-crashed", null, TimedOut: false));
        var scanned = WriteAndScan(TempProject.CmdMarkdown());

        await CreateRunner().RunAsync(scanned, new AgentSessionState());

        Assert.Equal("s-crashed", _agent.ResumeIds[1]);
        Assert.DoesNotContain("Do something useful.", _agent.Prompts[1]);
        Assert.Contains("failed while planning", _agent.Prompts[1]);
    }

    public void Dispose() => _project.Dispose();
}
