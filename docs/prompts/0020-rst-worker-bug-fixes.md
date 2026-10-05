---
title: "mf-worker: re-ready resets attempts, plan retry keeps the command, CLI follows the specs, one question stays one question"
cmd: 0020-cmd-worker-bug-fixes.md
done: 2026-10-05
summary: "Fixed all four mf-worker BUGs: a re-readied aborted command gets a fresh attempt budget and a new rst, a session-less retry resends the command, the CLI is strict (usage errors exit 2), run-all shows real statuses, and a multi-line pln question stays one qa question. Roll out with install.ps1 by hand."
---

# Report

## What was done

All five BUG lines named in the command are gone from the specs and the behaviour is implemented,
with tests. 189 worker tests pass (`dotnet test` in `tools/worker-controller`) and `spec_lint`
reports 0 problems.

1. **Re-run after abort** (`Execution/TaskRunner.cs`).
   - A run that starts with `attempts >= max_attempts` is a re-run. The claim commit writes
     `attempts: 0` and deletes the old rst; `git add -- <rst>` stages the deletion, which I
     checked against real git.
   - `BuildInitial(..., rerunAfterAbort: true)` tells the agent to write the rst from scratch,
     with the re-run note as the first line under "What was done".
     `RstFile.WriteFallback(..., rerunAfterAbort)` does the same if the agent writes no rst.
   - The abort reason moved into the new public helper `TaskRunner.AbortReason(...)`. It says
     "no phase ran: …" instead of naming an empty phase.
2. **Retry prompt** (`TaskRunner`, `PromptBuilder.BuildFailureFeedback`).
   - When the failed attempt left no session to resume, both loops now send
     `BuildPlan`/`BuildInitial` plus the feedback. A resumable session still gets only the
     feedback.
   - A plan-phase crash now records the phase as `plan` (it was `agent`). The feedback is worded
     per phase: `plan`, `agent`, then `build`/`tests` (the old text).
3. **CLI** (new `Commands/WorkerCli.cs`; `Program.cs` now only calls it).
   - Uses `UseStrictParsing()` plus `SetExceptionHandler`. A `CommandAppException` prints
     `mf-worker: <message>` plus usage lines on stderr and exits 2.
   - Help text now comes from the command classes' `[Description]`s; the `.WithDescription`
     overrides are gone.
   - `RunAllExecutor.CommandOutcome` gained `Status`, read back from the cmd file by re-scanning
     after each run. The run-all table columns are now `Command | Status | Exit`.
   - I checked the built binary by hand: `--help` → 0; `run --bogus x`, `run a b`, `run`,
     `frob` → 2 with the message on stderr.
4. **Questions** (`Prompts/PlnFile.cs`).
   - `ReadOpenQuestions` now groups lines into questions. A `-` bullet at the indent of the
     section's first bullet (or less) starts a new question; every other line belongs to the
     current one, and lines before the first bullet are dropped.
   - Only the top-level `- ` marker is stripped. All other lines keep their indent, and lines
     are joined with `Environment.NewLine`.
   - `QaFile` is unchanged; it already wrote the string as given.
5. **Tests**:
   - `TaskRunnerTests`: re-run, resume keeps its count, `AbortReason`, plan-crash retry with and
     without a session.
   - `PromptBuilderTests`: wording per phase.
   - New `CliTests`: usage errors → 2, `--help` → 0 with lane wording.
   - `RunAllTests`: status column shows `questions` for a pause.
   - `PlnFileTests`: the 0019 shape → one question; two one-line questions → two.
   - `QaFileTests`: qa written from the 0019 shape has one round with nested lines intact.
6. **Specs**:
   - `concepts/command-lifecycle.md`: the re-run rule; resume ≠ re-run; the abort reason; what
     one question is and how it is copied into the qa file; the `attempts` table row.
   - `concepts/worker-run.md`: the reset; the retry prompt; feedback per phase.
   - `worker/run.md`: a new "Argument parsing" section; usage errors added to exit code 2.
   - `worker/next.md` and `worker/run-all.md`: the help wording and the status table.
   - `STATUS.md`: a recent-updates line.

## Decisions and deviations

- **How a re-run is detected:** only by `attempts >= max_attempts`, as the command's decision
  states. It does not check whether the previous status was `aborted`.
  - A command paused at `questions` always has attempts left, so it never meets the reset.
  - Exception: a human who lowers `max_attempts` on a paused command below its count would
    trigger the reset. I accepted that.
- **The rst is deleted at claim time, not at the end of the run.** That way a pause or a
  `mf-run stop` refusal after the claim still leaves a clean tree. The agent cannot "update" a
  stale report, and the fallback is not skipped because an old file exists.
- **"No phase ran" can no longer happen through `RunAsync`.** The reset guarantees at least one
  plan call, so the message is tested on the helper directly.
- **Non-usage exceptions** in the exception handler return -1, as Spectre did before. Mapping
  crashes to another exit code was out of scope.
- **Run-all table:** I added an `Exit` column next to `Status`, so a usage error (status still
  `ready`, exit 2) remains visible.

## Skipped / uncertain

- I did not run `install.ps1`, by design. The new worker reaches the watcher machine only after
  a manual rollout.
- `-h` is assumed to behave like `--help` (Spectre's built-in); only `--help` is tested.
- The specs touched remain marked DRAFT; this run did not human-review them.

## Self-answered questions

- **How should "the new rst overwrites the old one" be enforced, since the agent writes the
  rst?** The worker deletes the old rst in the claim commit, and the implementation prompt
  tells the agent it is a re-run and must write the rst from scratch, starting with the re-run
  note.
- **How does the worker know it is a re-run after `aborted`?** Only from
  `attempts >= max_attempts` at the start of a run from `ready`, the condition the command
  itself gives.
- **What should the abort reason say when no phase ran?** "no phase ran: N of M attempt(s) were
  already used before this run could start one".
- **Which phase name does a plan crash carry?** `plan`. It drives the plan-specific feedback,
  and the abort reason reads "planning failed …; last failing phase: plan".
- **What does "prints usage" on a usage error mean concretely?** The error message plus a fixed
  block of usage lines, both on stderr (design.md shared conventions).
- **Should exceptions that are not parse errors change exit code?** No; they keep Spectre's
  previous -1. That is outside this command's scope.
- **What is "verbatim" for a question?** Every line keeps its indentation except that the
  top-level `- ` marker is dropped from the first line, so nested options stay nested under the
  question in the qa file.
- **Where is the "top level" when a pln indents its list?** At the indent of the section's first
  bullet.
