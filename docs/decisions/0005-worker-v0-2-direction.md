---
date: 2026-07-07
topic: worker
status: accepted
---

# 0005 — Worker Controller v0.2 — direction

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

With Spec Kit retired from the default workflow (`0002-spec-strategy.md`),
the Worker Controller became relevant again as the execution half of
the original loop: talk at a high level against `docs/` only, update
state specs and write a task, let the worker run it, read the result
back, ground the next conversation in it. The separation of planes is
deliberate: talking against `docs/` only keeps the conversation free of
code detail, and a detail that *does* matter is the signal that it
belongs in a spec.

v0.1 worked. Practice showed three gaps: the worker guessed where it
should have asked, its result was a log rather than something the next
conversation could read, and it lacked the guardrails a human session
gets from CLAUDE.md.

A hard constraint, verified against the Claude Code docs (2026-07):
headless `claude -p` is strictly non-interactive. An agent cannot pause
to ask, and an unapproved permission prompt aborts the run.

## Options

- For questions: let the agent guess and report afterwards; or a plan
  step that answers what it can itself and stops for what is genuinely
  the human's.
- For pausing: keep the process waiting; or stop-and-resume — write
  the questions, set a status, exit (one-shot stays intact), and on the
  next run continue the same agent session with the human's answers.
- For permissions: let the worker manage them; or pre-approve them in
  project config, since a denied tool does not ask, it kills the run.

## Measurement

Fit with the one-shot and plain-text principles; no lost context across
a pause; and the same question gate as everywhere else in MagnaFlow
(`docs/context/2026-07-07-cockpit-multi-project-idea.md`).

## Choice

1. **Plan step with questions**, stop-and-resume on the same session,
   using the prompt lane's own `questions` status; the human answers by
   editing a file.
2. **A readable report per task**: what was done, decisions taken while
   implementing, anything skipped or uncertain — written for the next
   conversation to read, so it can continue from the report instead of
   re-reading the diff.
3. **Conventions in the prompt**: the target project's
   constitution/conventions travel with the task and linked specs.
4. **Permissions pre-approved** via the agent arguments in config; the
   worker adds none itself.

Not in scope: dispatcher and parallel workers, unchanged from v0.1.

**Invariant: the controller stays optional.** The manual path — copy a
prompt into Claude Code by hand — remains first-class, permanently. The
controller executes the same plain-text formats and is never a
gatekeeper: nothing (plans, questions, reports, statuses) may become
readable or writable only through it. If a mechanism cannot also be
done by a human editing files, the mechanism is wrong.

The original sketched a JSON-schema reply contract and task-folder
files; the built mechanism differs. Current behavior:
specs/concepts/worker-run.md, specs/concepts/command-lifecycle.md,
specs/concepts/session-resume.md.
