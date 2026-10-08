---
date: 2026-07-02
topic: worker, workflow
status: accepted
---

# 0001 — MagnaFlow — Workflow v0.1 (pinned down for the Worker Controller)

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

The Worker Controller was about to be built (with GitHub Spec Kit, as
the first MagnaFlow tool). It needed just enough workflow to know where
to scan, what to pick up and where output lands. Anything not decided
here was deliberately left open. The statuses `proposed`, `accepted`,
`reviewing` from the vision documents were kept out: they belong to the
spec layer, not the execution layer.

## Options

- **Where a task's status lives**: a central queue/index file, or YAML
  frontmatter in the task file itself.
- **How the tool runs**: a long-running service that polls and picks
  work, or a one-shot program that does one job and stops.
- **Where a task's files live**: separate `queue/` and `runs/` folders
  (the task moves between them), or one folder per task holding
  definition, status and output together.

## Measurement

Git as the only database: a task's whole life cycle must be readable
from `git diff`, task and status must never get out of sync, and files
should never move. Small deterministic tools that compose, so that
later orchestration (polling, priority, other machines) can be added
around a tool instead of inside it.

## Choice

1. **Status in frontmatter**, one markdown file per task, changed in
   place. The filename/folder name is the id; no `id:` field, so it can
   never disagree.
2. **One-shot, as a permanent principle.** Polling and deciding "what
   next" belong to a separate dispatcher that calls the controller —
   which then makes distributed workers almost free.
3. **One folder per task** (revised the same day from separate queue
   and runs folders): files never move, order lives in the four-digit
   numbering, and an unrun task is visibly just its definition.

Deliberately not in v0.1: the dispatcher, locking and parallel workers,
remote workers, a dashboard, a spec worker, automatic PRs or merges
(the human reviews the branch).

Later revised: tasks became commands in the `docs/prompts/` lane, with
runtime evidence in `.magnaflow/<id>/` kept machine-local rather than
committed; the dispatcher became mf-watch and the dashboard mf-cockpit.
Current behavior: specs/concepts/design.md (principles),
specs/concepts/command-lifecycle.md, specs/concepts/worker-run.md,
specs/concepts/evidence-layout.md, specs/concepts/session-resume.md,
specs/worker/.
