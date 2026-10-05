---
date: 2026-07-07
topic: cockpit
source: own observations from Spec Kit use on ProjectA
---

# Cockpit — multi-project management (idea, 2026-07-07)

Captured from real Spec Kit usage on ProjectA. Two observations:

## Auto-run mode for the pipeline

The Spec Kit steps are valuable but the waiting between them is mostly
human review time. For low-risk features the gates can be automated:
after each step the LLM checks "are there concrete questions for the
human?" — if yes, stop and ask; if no, continue to the next step. The
human reviews the end result only. This is the same judgement structure
as the drift check (deterministic continue, AI only at decision points),
and a natural mode for the dispatcher to drive later.

## Multi-project cockpit

Working on several projects at once makes it hard to know where you are:
which feature, which pipeline step, what is waiting for whom. Wanted:

- One overview across projects: active feature, current step
  (specify/clarify/plan/tasks/implement/fold-in), whether it is blocked
  on a human answer.
- Fast workspace switching (project + branch + session in one action).
- Optionally: parallel features per project via git worktrees, for
  features that don't touch each other (lower conviction — revisit when
  the need is real).

This is the "glasbox" dashboard from the phase-1 brainstorm, now with
concrete requirements from practice. It naturally combines with the
dispatcher: the same state the dispatcher needs (who is at bat, per
task/feature) is what the cockpit displays. Most of that state already
exists in plain text: `.specify/feature.json`, branch names, task
frontmatter, STATUS.md — a first cockpit could be a read-only script
that aggregates these across repos.
