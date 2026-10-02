using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Infrastructure;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Execution;

/// <summary>
/// Carries agent-session continuity between consecutive commands in one controller invocation
/// (v0.1 FR-013). Deliberately in-memory only: a new invocation always starts fresh sessions
/// unless a command's own recorded session applies (self-resume, spec FR-013).
/// </summary>
public sealed class AgentSessionState
{
    public string? SessionId { get; set; }
    public string? Group { get; set; }
}

/// <summary>
/// The execution loop for one command (spec User Stories 1-3), following the two-plane commit
/// model: bookkeeping (docs/prompts/ — cmd/pln/qa/rst) commits on the invoking branch; the
/// agent's code work commits on the command's work branch (spec FR-004, docs/mf-spec/system.md).
/// Every run first runs a plan phase — a separate agent invocation, still on the invoking
/// branch, since it only reads/writes bookkeeping files — that gates whether execution (which
/// does need the work branch) proceeds this run or the command pauses at `questions` instead
/// (spec FR-008-FR-011a). Class kept as "TaskRunner" (not renamed) per plan.md's structure
/// decision: it orchestrates a command's run, not a single "task" concept, but renaming the
/// class itself was not required for the vocabulary migration.
/// </summary>
public sealed class TaskRunner(
    ProjectConfig config,
    IGitClient git,
    IAgentRunner agent,
    IProcessRunner processes,
    string projectRoot,
    Action<string> info,
    Action<string>? errorSink = null)
{
    /// <summary>Diagnostics that must not scroll past in grey: stderr, like the CLI's own.</summary>
    private readonly Action<string> error = errorSink ?? (m => Console.Error.WriteLine($"mf-worker: {m}"));

    public async Task<int> RunAsync(ScannedCommand scanned, AgentSessionState session)
    {
        // --- Preconditions, in contract order; no mutation before all pass (contracts/cli.md) ---
        if (scanned.IsMalformed)
        {
            info($"[{scanned.Id}] malformed: {scanned.Error}");
            return ExitCodes.UsageError;
        }
        var cmd = scanned.Cmd!;

        if (!await git.IsAvailableAsync())
        {
            info("git is not available on this machine");
            return ExitCodes.EnvironmentError;
        }
        if (!await agent.IsAvailableAsync(projectRoot))
        {
            info($"agent '{config.AgentCommand}' is not available; install/authenticate it or set agent.command in .magnaflow/config.yml");
            return ExitCodes.EnvironmentError;
        }
        if (!await git.IsWorkingTreeCleanAsync())
        {
            info("working tree has uncommitted changes; commit or stash them first (command stays ready)");
            return ExitCodes.PreconditionRefused;
        }

        // Never orchestrate from a command's work branch: its bookkeeping is a frozen snapshot
        // from mid-run, so the queue would be misread and command state would be written onto
        // the work branch (v0.1 FR-006a, unchanged).
        var invokingBranch = await git.GetCurrentBranchAsync();
        var workBranchOwner = PromptScanner.Scan(projectRoot)
            .FirstOrDefault(c => c.Cmd?.Branch is not null && c.Cmd.Branch == invokingBranch);
        if (workBranchOwner is not null)
        {
            var home = await git.GetDefaultBranchAsync() ?? "<your orchestration branch>";
            info($"refused: you are on '{invokingBranch}', the work branch of command {workBranchOwner.Id}.");
            info($"Command state on a work branch is a frozen mid-run snapshot — running from here would misread the queue and pollute the branch with orchestration commits.");
            info($"Switch back first:  git checkout {home}");
            return ExitCodes.PreconditionRefused;
        }

        if (cmd.Status != CmdStatus.Ready)
        {
            info(cmd.Status == CmdStatus.Running
                ? $"[{cmd.Id}] is marked running — a previous run may have been interrupted; inspect the command and reset status to ready manually if appropriate"
                : $"[{cmd.Id}] has status '{cmd.Status.ToYaml()}', only ready commands can run"
                  + (cmd.Status == CmdStatus.Questions ? " — answer its questions file and set status back to ready first" : ""));
            return ExitCodes.PreconditionRefused;
        }
        var missingSpecs = PromptBuilder.FindMissingSpecs(cmd, projectRoot);
        if (missingSpecs.Count > 0)
        {
            info($"[{cmd.Id}] linked spec file(s) not found: {string.Join(", ", missingSpecs)}");
            return ExitCodes.UsageError;
        }
        string? explicitResumeSession = null;
        if (cmd.Resume is not null)
        {
            var (resolved, resumeError) = ResumeResolver.Resolve(cmd.Resume, projectRoot);
            if (resumeError is not null)
            {
                info($"[{cmd.Id}] {resumeError}");
                return ExitCodes.UsageError;
            }
            explicitResumeSession = resolved;
        }

        string? baseBranch = null;
        if (cmd.Branch is not null && !await git.BranchExistsAsync(cmd.Branch))
        {
            baseBranch = cmd.Base ?? await git.GetDefaultBranchAsync();
            if (baseBranch is null)
            {
                info($"[{cmd.Id}] no base branch: set 'base:' in the command or create a main/master branch");
                return ExitCodes.UsageError;
            }
            if (!await git.BranchExistsAsync(baseBranch))
            {
                info($"[{cmd.Id}] base branch '{baseBranch}' does not exist");
                return ExitCodes.UsageError;
            }
        }

        // --- Execution: from here on, state changes are committed (constitution III) ---
        string RelPath(string absolute) => Path.GetRelativePath(projectRoot, absolute).Replace('\\', '/');

        // All state lives in git, so when a remote is configured both planes are pushed: the work
        // branch after the work commit, the invoking branch after each bookkeeping commit. A failed
        // push (offline, protected branch) is a warning, never a failed command — everything is
        // already committed locally.
        var remote = await git.GetRemoteAsync();
        async Task PushToRemoteAsync(string branch)
        {
            if (remote is null) return;
            try
            {
                info($"[{scanned.Id}] pushing {branch} to {remote}");
                await git.PushAsync(remote, branch);
            }
            catch (GitException ex)
            {
                info($"[{scanned.Id}] push of {branch} to {remote} failed ({ex.Message}) — commits remain local");
            }
        }

        // mf-run stop/start (docs/fase6-mf-run/ontwerp-v0.1.md "Worker integration"): process spawn
        // and exit code only, no library reference either way. A spawn failure (mf-run not
        // installed/found) is treated the same as mf-run itself exiting non-zero.
        async Task<ProcessResult> RunMfRunAsync(string verb)
        {
            try
            {
                return await processes.RunExecutableAsync(config.RunCommand, [verb, "--project", projectRoot], projectRoot);
            }
            catch (Exception ex)
            {
                return new ProcessResult(-1, "", ex.Message, TimedOut: false);
            }
        }

        var started = DateTimeOffset.Now;
        var cmdPathRelative = RelPath(cmd.FilePath);
        var workCommitMessage = $"command {cmd.Id}: {cmd.Title}";
        var evidenceDirectory = CmdFile.EvidenceDirectory(projectRoot, cmd.Id);
        var evidenceDirRelative = RelPath(evidenceDirectory);

        info($"[{cmd.Id}] {cmd.Title} — starting");
        cmd.WriteStatus(CmdStatus.Running);
        await git.StagePathAsync(cmdPathRelative);
        await git.CommitAsync($"mf-worker: {cmd.Id} -> running");

        // A branchless run lands as a single commit: this claim commit is amended at the end to
        // carry the work as well (docs/mf-spec/system.md "Branches"). Remember what it was, so the
        // amend can prove it is still the commit it is about to rewrite. In branch mode the two
        // planes are genuinely separate commits, so there is nothing to fold.
        var claimCommit = cmd.Branch is null ? await git.GetHeadCommitAsync() : null;

        // Session continuity, in order of precedence (spec FR-013 amends v0.1 FR-013a):
        // 1. explicit resume: (raw session ID or another command's recorded session)
        // 2. same group as the immediately preceding command in this invocation (v0.1 FR-013)
        // 3. this command's own recorded session from a previous paused run (self-resume, NEW)
        string? resumeSession;
        if (explicitResumeSession is not null)
        {
            resumeSession = explicitResumeSession;
            info($"[{cmd.Id}] resuming agent session {(cmd.Resume != explicitResumeSession ? $"recorded by {cmd.Resume}" : explicitResumeSession)}");
        }
        else if (!cmd.FreshSession && cmd.Group is not null && cmd.Group == session.Group)
        {
            resumeSession = session.SessionId;
        }
        else
        {
            resumeSession = SessionEvidence.ReadSessionId(evidenceDirectory);
            if (resumeSession is not null)
                info($"[{cmd.Id}] resuming this command's own recorded session (self-resume)");
        }

        var maxAttempts = Math.Max(1, cmd.MaxAttempts ?? config.MaxAttempts);
        var attemptsUsed = cmd.Attempts;
        var conventions = ConventionLoader.Load(projectRoot);

        var paused = false;
        var planGatePassed = false;
        var stopRefused = false;
        var succeeded = false;
        var workStaged = false;
        string? feedbackPhase = null;
        string? feedbackOutput = null;

        // Writers MUST be disposed before the final git add: FileStream buffers internally,
        // so an open log file can still look empty (or stale) to git (v0.1 research, unchanged).
        Directory.CreateDirectory(evidenceDirectory);
        using (var claudeLog = new AttemptLogWriter(Path.Combine(evidenceDirectory, "claude.log")))
        using (var buildLog = new AttemptLogWriter(Path.Combine(evidenceDirectory, "build.log")))
        using (var testLog = new AttemptLogWriter(Path.Combine(evidenceDirectory, "test.log")))
        {
            // === PLAN PHASE (spec FR-008-FR-011a): always runs first, once per run, still on the
            // invoking branch since it only reads/writes bookkeeping, never product code. ===
            while (attemptsUsed < maxAttempts && !planGatePassed && !paused)
            {
                var now = DateTimeOffset.Now;
                claudeLog.WritePhaseHeader("plan", now);

                var planPrompt = feedbackPhase is null
                    ? PromptBuilder.BuildPlan(cmd, projectRoot, conventions)
                    : PromptBuilder.BuildFailureFeedback(feedbackPhase, feedbackOutput!);

                info($"[{cmd.Id}] planning{(resumeSession is not null ? " (resumed session)" : "")}");
                var planResult = await agent.RunAsync(planPrompt, resumeSession, projectRoot, claudeLog.WriteLine);
                resumeSession = planResult.SessionId ?? resumeSession;

                if (!planResult.Succeeded)
                {
                    attemptsUsed++;
                    feedbackPhase = "agent";
                    feedbackOutput = planResult.TimedOut
                        ? $"the agent run timed out after {config.CommandTimeout.TotalMinutes:0} minutes"
                        : $"the agent process exited with code {planResult.ExitCode}";
                    info($"[{cmd.Id}] agent {(planResult.TimedOut ? "timed out" : "crashed")} during planning — counts as a failed attempt");
                    continue;
                }

                var openQuestions = PlnFile.ReadOpenQuestions(cmd.PlnPath);
                if (openQuestions.Count > 0)
                {
                    QaFile.AppendQuestions(cmd.QaPath, openQuestions);
                    paused = true;
                    info($"[{cmd.Id}] paused: {openQuestions.Count} question(s) need a human answer — see {Path.GetFileName(cmd.QaPath)}");
                    break;
                }

                planGatePassed = true; // nothing genuinely open — continue straight into execution (FR-010)
            }

            if (planGatePassed)
            {
                // Stop immediately before implementation, not at the start of the run: the plan
                // phase touches no code, so the app keeps running while planning (and through a
                // questions pause, since that path never reaches here at all). A stop failure
                // refuses to start work, same posture as the dirty-tree guard — the locked ports
                // the stop was meant to release are still locked (ontwerp-v0.1.md "Worker
                // integration"); status revert + commit happens after the log writers are disposed,
                // below, so this leaves a clean tree rather than a dirty one for the next run.
                if (config.HasRunServices)
                {
                    info($"[{cmd.Id}] stopping running services ({config.RunCommand} stop)");
                    var stopResult = await RunMfRunAsync("stop");
                    if (!stopResult.Succeeded)
                    {
                        info($"[{cmd.Id}] {config.RunCommand} stop failed (exit {stopResult.ExitCode}); refusing to start implementation (command stays ready)");
                        stopRefused = true;
                    }
                }

                if (!stopRefused)
                {
                    if (cmd.Branch is null)
                    {
                        // Branchless mode: no work branch in the frontmatter, so the agent's work
                        // lands directly on the invoking branch, folded into the claim commit.
                        info($"[{cmd.Id}] no work branch configured — working directly on '{invokingBranch}'");
                    }
                    else if (baseBranch is null)
                    {
                        info($"[{cmd.Id}] reusing existing branch {cmd.Branch}");
                        await git.CheckoutAsync(cmd.Branch);
                    }
                    else
                    {
                        info($"[{cmd.Id}] creating branch {cmd.Branch} from {baseBranch}");
                        await git.CreateBranchAsync(cmd.Branch, baseBranch);
                    }

                    // === EXECUTION PHASE: implement, then build/test with bounded retries (v0.1 mechanics) ===
                    feedbackPhase = null;
                    feedbackOutput = null;

                    // A section's commands: run sequentially; the first failure stops the sequence and
                    // its own output is the feedback (contracts/file-formats.md `.magnaflow/config.yml`).
                    async Task<ProcessResult> RunSequentiallyAsync(IReadOnlyList<string> commands, Action<string> onOutputLine)
                    {
                        var result = new ProcessResult(0, "", "", false);
                        foreach (var command in commands)
                        {
                            result = await processes.RunShellAsync(command, projectRoot, onOutputLine, config.CommandTimeout);
                            if (!result.Succeeded)
                                break;
                        }
                        return result;
                    }
                    while (attemptsUsed < maxAttempts && !succeeded)
                    {
                        attemptsUsed++;
                        var now = DateTimeOffset.Now;
                        claudeLog.WriteAttemptHeader(attemptsUsed, maxAttempts, now);
                        buildLog.WriteAttemptHeader(attemptsUsed, maxAttempts, now);
                        testLog.WriteAttemptHeader(attemptsUsed, maxAttempts, now);

                        var prompt = feedbackPhase is null
                            ? PromptBuilder.BuildInitial(cmd, projectRoot, conventions)
                            : PromptBuilder.BuildFailureFeedback(feedbackPhase, feedbackOutput!);

                        info($"[{cmd.Id}] attempt {attemptsUsed}/{maxAttempts}: agent working{(resumeSession is not null ? " (resumed session)" : "")}");
                        var agentResult = await agent.RunAsync(prompt, resumeSession, projectRoot, claudeLog.WriteLine);
                        resumeSession = agentResult.SessionId ?? resumeSession;

                        if (!agentResult.Succeeded)
                        {
                            feedbackPhase = "agent";
                            feedbackOutput = agentResult.TimedOut
                                ? $"the agent run timed out after {config.CommandTimeout.TotalMinutes:0} minutes"
                                : $"the agent process exited with code {agentResult.ExitCode}";
                            info($"[{cmd.Id}] agent {(agentResult.TimedOut ? "timed out" : "crashed")} — counts as a failed attempt");
                            continue;
                        }

                        info($"[{cmd.Id}] attempt {attemptsUsed}/{maxAttempts}: build");
                        var build = await RunSequentiallyAsync(config.BuildCommands, buildLog.WriteLine);
                        if (!build.Succeeded)
                        {
                            feedbackPhase = "build";
                            feedbackOutput = build.TimedOut ? "build timed out" : build.StdOut + "\n" + build.StdErr;
                            info($"[{cmd.Id}] build failed");
                            continue;
                        }

                        info($"[{cmd.Id}] attempt {attemptsUsed}/{maxAttempts}: tests");
                        var test = await RunSequentiallyAsync(config.TestCommands, testLog.WriteLine);
                        if (!test.Succeeded)
                        {
                            feedbackPhase = "tests";
                            feedbackOutput = test.TimedOut ? "tests timed out" : test.StdOut + "\n" + test.StdErr;
                            info($"[{cmd.Id}] tests failed");
                            continue;
                        }

                        succeeded = true;
                    }

                    // --- Work plane: commit the agent's code changes (never .magnaflow/ or docs/prompts/) ---
                    // Branchless, the work stays staged and rides along to the terminal commit below;
                    // nothing may be committed or pushed between the claim commit and its amend.
                    await git.StageAllExceptBookkeepingAsync();
                    workStaged = cmd.Branch is null;
                    if (cmd.Branch is not null)
                    {
                        if (await git.HasStagedChangesAsync())
                            await git.CommitAsync(workCommitMessage);
                        await PushToRemoteAsync(cmd.Branch);
                        await git.CheckoutAsync(invokingBranch);
                    }
                }
            }
        } // logs flushed & closed here, before any staging (v0.1 bug-fix invariant)

        if (stopRefused)
        {
            cmd.WriteAttempts(attemptsUsed);
            cmd.WriteStatus(CmdStatus.Ready);
            await git.StagePathAsync(cmdPathRelative);
            if (File.Exists(cmd.PlnPath))
                await git.StagePathAsync(RelPath(cmd.PlnPath));
            await git.StageIfPresentAsync(evidenceDirRelative);
            await git.CommitAsync($"mf-worker: {cmd.Id} -> ready ({config.RunCommand} stop failed)");
            await PushToRemoteAsync(invokingBranch);
            return ExitCodes.PreconditionRefused;
        }

        if (paused)
        {
            SessionEvidence.Write(evidenceDirectory, resumeSession);
            cmd.WriteAttempts(attemptsUsed); // unchanged unless a plan-phase crash consumed one (FR-011a)
            cmd.WriteStatus(CmdStatus.Questions);
            await git.StagePathAsync(cmdPathRelative);
            if (File.Exists(cmd.PlnPath))
                await git.StagePathAsync(RelPath(cmd.PlnPath));
            await git.StagePathAsync(RelPath(cmd.QaPath));
            await git.StageIfPresentAsync(evidenceDirRelative);
            await git.CommitAsync($"mf-worker: {cmd.Id} -> questions");
            await PushToRemoteAsync(invokingBranch);

            session.SessionId = resumeSession;
            session.Group = cmd.Group;
            return ExitCodes.Success; // a legitimate pause is not a failure (research R8)
        }

        // --- Terminal outcome: done (planned + succeeded) or aborted (any other non-success reason) ---
        string? abortReason = !planGatePassed
            ? $"planning failed after {attemptsUsed} attempt(s); last failing phase: {feedbackPhase}"
            : !succeeded
                ? $"{feedbackPhase} kept failing after {attemptsUsed} attempt(s)"
                : null;
        var isDone = planGatePassed && succeeded;

        SessionEvidence.Write(evidenceDirectory, resumeSession);
        cmd.WriteAttempts(attemptsUsed);
        cmd.WriteStatus(isDone ? CmdStatus.Done : CmdStatus.Aborted);
        if (!RstFile.Exists(cmd.RstPath)) // FR-017's "MUST ensure" — a safety net, not the agent's normal path
            RstFile.WriteFallback(cmd.RstPath, isDone, attemptsUsed, abortReason, cmd.Title);

        // Start after any terminal status, done and aborted both — the last successful build may
        // still be perfectly runnable, and looking at it is often how a human diagnoses an aborted
        // run (ontwerp-v0.1.md "Worker integration"). A start failure is a warning, never a retry
        // and never a status change: append to the rst (before it's staged below) and move on.
        if (config.HasRunServices)
        {
            info($"[{cmd.Id}] starting services ({config.RunCommand} start)");
            var startResult = await RunMfRunAsync("start");
            if (!startResult.Succeeded)
            {
                RstFile.AppendWarning(cmd.RstPath, $"{config.RunCommand} start failed (exit {startResult.ExitCode}):\n{startResult.StdOut}{startResult.StdErr}");
                info($"[{cmd.Id}] {config.RunCommand} start failed (exit {startResult.ExitCode}); see {Path.GetFileName(cmd.RstPath)} for details");
            }
        }

        // Fold the whole branchless run into the claim commit — only when there was a work plane
        // at all (a run that never got past planning is already one commit, and its message should
        // say so), and only where amending is provably safe, checked here and nowhere earlier: HEAD must still be exactly the commit this run
        // wrote (the agent may have committed on its own, and a resumed run starts from a tree the
        // worker did not just commit), and that commit must not have reached a remote, or the
        // amend would turn into a force-push. Either condition failing is not an error: the run
        // simply lands as two commits, the way it always did.
        var fold = workStaged
                   && claimCommit is not null
                   && await git.GetHeadCommitAsync() == claimCommit
                   && !await git.IsOnRemoteTrackingRefAsync(claimCommit);
        if (!fold && workStaged && await git.HasStagedChangesAsync())
            await git.CommitAsync(workCommitMessage);

        // The one durable output of the whole run. Losing it is the worst outcome the worker has —
        // the work is on disk, the status says done, and nothing is recorded — so it does not get
        // to end as one grey line at the bottom of a 25-minute scroll.
        try
        {
            await git.StagePathAsync(cmdPathRelative);
            if (File.Exists(cmd.PlnPath))
                await git.StagePathAsync(RelPath(cmd.PlnPath));
            await git.StagePathAsync(RelPath(cmd.RstPath));
            await git.StageIfPresentAsync(evidenceDirRelative);
            if (fold)
                await git.AmendCommitAsync(workCommitMessage);
            else
                await git.CommitAsync($"mf-worker: {cmd.Id} -> {(isDone ? "done" : "aborted")} ({attemptsUsed} attempt(s))");
        }
        catch (GitException ex)
        {
            error($"[{cmd.Id}] the run finished ({(isDone ? "done" : "aborted")} after {attemptsUsed} attempt(s)) but its commit FAILED: {ex.Message}");
            error($"[{cmd.Id}] {Path.GetFileName(cmd.FilePath)} and {Path.GetFileName(cmd.RstPath)} are written but uncommitted, and the work is still in the tree.");
            error($"[{cmd.Id}] nothing was pushed. The next run will refuse on a dirty working tree until someone commits this by hand.");
            return ExitCodes.EnvironmentError;
        }
        await PushToRemoteAsync(invokingBranch);

        session.SessionId = resumeSession;
        session.Group = cmd.Group;

        info($"[{cmd.Id}] {(isDone ? "done" : "ABORTED")} after {attemptsUsed} attempt(s); work {(cmd.Branch is null ? $"on '{invokingBranch}'" : $"branch: {cmd.Branch}")}");
        return isDone ? ExitCodes.Success : ExitCodes.TaskFailed;
    }
}
