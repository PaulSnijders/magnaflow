---
title: "mf-worker: re-ready resets attempts, plan retry keeps the command, CLI follows the specs, one question stays one question"
status: ready
created: 2026-10-05
---

## Context

The initial spec sync left BUG lines in `docs/specs/worker/` and in two
worker concepts. Fix them in this run, step by step, each with its own
tests. Read the claim and retry logic in `TaskRunner` before step 1.

## Task

1. **A command set back to `ready` gets a fresh attempt budget**
   (`docs/specs/concepts/command-lifecycle.md`).
   - The problem: `attempts:` counts across runs and nothing resets it.
     A re-readied `aborted` command is claimed and aborted at once,
     without calling the agent, with an empty "last failing phase". The
     old rst is kept.
   - Decision (already taken): setting `ready` is the human's "try
     again".
     - A run that starts from `ready` with `attempts >= max_attempts`
       resets `attempts` to 0.
     - The new run's rst overwrites the old one, which stays in git
       history. Its first line under "What was done" notes that this is
       a re-run after an earlier `aborted`.
     - A resume from `questions` → `ready` is unchanged: it continues the
       same run and keeps counting.
   - An abort reason never has an empty "last failing phase". If no
     phase ran, say so.
2. **A plan-phase retry without a session resends the command**
   (`docs/specs/concepts/worker-run.md`).
   - The problem: when a plan crash returns no session id, the retry's
     fresh session gets only `BuildFailureFeedback("agent", …)`, without
     the command body. The feedback also says "failed after your
     changes", while there were none.
   - The fix: a retry that cannot resume a session builds the full
     initial prompt for the phase it retries (`BuildPlan` or
     `BuildInitial`) and appends the feedback.
   - Word the feedback per phase.
3. **The CLI follows the specs** (`docs/specs/worker/run.md`, `next.md`,
   `run-all.md`).
   - Parsing: unknown options and arguments are errors. Every usage
     error exits 2 (`ExitCodes.UsageError`) and prints its message on
     stderr, per `docs/specs/concepts/design.md#shared-conventions`.
     Today Spectre's defaults ignore unknown options and exit 127.
     `--help` still exits 0.
   - `--help` text: `Program.cs` still says "first pending task in
     folder-name order". Use the command classes' `[Description]`s, or
     rewrite the text in commands / `ready` / id-order terms.
   - `run-all`: the closing table shows each command's resulting status,
     read back from its cmd file. Today it maps exit codes, so a
     `questions` pause shows as `done`.
4. **One question in the pln is one question in the qa file**
   (`docs/specs/concepts/command-lifecycle.md`).
   - The problem, seen live in 0019: the pln's "Open questions (this
     round)" held one question, a multi-line top-level bullet with
     nested options (A) and (B). `PlnFile.ReadOpenQuestions` turns
     *every* line that starts with `- ` into a question, at any indent,
     and keeps only that one line (`BulletLine`). The qa file therefore
     got three "rounds", each a truncated first line: the question, then
     option (A), then option (B). The human could not answer them
     without opening the pln.
   - The fix: a question is a **top-level** bullet together with
     everything under it: its continuation lines and nested bullets, up
     to the next top-level bullet or the end of the section. Trailing
     text after the list (a recommendation paragraph) belongs to the
     last question. `QaFile.AppendQuestions` writes that block
     verbatim, so its nested lines stay nested.
   - Keep the on-disk qa format (`## Question (round N)` +
     `**Answer**:`) unchanged; cockpit and humans read it.
5. **Tests** in the worker test project, for each step:
   - step 1: a re-readied exhausted command runs the agent again; a
     resume after questions keeps its count; the abort message when no
     phase ran;
   - step 2: the second agent call carries the command body;
   - step 3: usage errors exit 2; the run-all status column;
   - step 4: `ReadOpenQuestions` on the 0019 shape (one multi-line
     question with nested (A)/(B) options and a trailing recommendation
     → exactly one question, all text kept); two plain one-line
     questions → two; the qa file written from the first case.
6. **Spec-first:** remove the BUG lines from `command-lifecycle.md`,
   `worker-run.md`, `run.md`, `next.md` and `run-all.md`, and describe
   the new behaviour there. In `command-lifecycle.md`, also state what
   counts as one question in the pln's open-questions section.
