---
date: 2026-07-02
topic: worker, workflow
status: accepted
---

# 0001 — MagnaFlow — Workflow v0.1 (pinned down for the Worker Controller)

> Migrated from `docs/decisions/0001-worker-workflow-v0-1.md` on 2026-10-05. Pre-dates the four-section
> decision format; original text unchanged.

> Purpose of this document: pin down just enough workflow so the Worker
> Controller knows **where it scans**, **what it picks up** and **where output
> lands**. Anything not stated here is deliberately not decided yet.
>
> **Status after implementation (2026-07-02):** the controller is built. The
> normative detail contracts (exact fields, config.yml, result.yml,
> log format) now live in `specs/001-worker-controller/contracts/` — on
> conflict, that contract wins. This document describes the why and the
> broad strokes.

## Design choices (decided 2026-07-02)

1. **Task status in YAML frontmatter** — one markdown file per task; status
   changes in the file itself. Why: git diff shows a task's full
   life cycle, and task + status can never get out of sync.
2. **One-shot execution — permanent design principle** — the controller does
   its job and stops. Polling, prioritizing and deciding "what next" later
   becomes the job of a separate **dispatcher (poll agent)** that calls the
   controller. Why: small deterministic tools that you compose; the
   dispatcher can later just as well call controllers on other machines
   (distributed workers almost for free).
3. **One folder per task, everything together** (revised 2026-07-02) — no separate
   queue and runs folders, but `.magnaflow/tasks/NNNN-name/` containing the
   task definition, status and all execution output. Why: files
   never move (git-friendly), the order lives in the numbering with
   leading zeros, and you can see at a glance per task whether it has run
   (only `task.md` = not run yet).

## Folder structure (in a target project)

```text
docs/                 # knowledge and specs (source of truth)
src/                  # implementation
tests/

.magnaflow/
  config.yml          # project config: build/test commands, retry limit
  tasks/
    0001-project-setup/
      task.md         # definition + status (frontmatter)
      claude.log      # raw agent output (after execution; name is v0.1 —
                      # candidate for renaming to agent.log in v0.2,
                      # because the agent is interchangeable)
      build.log
      test.log
      result.yml      # machine-readable final result
    0002-datamodel/
      task.md         # status: pending — folder contains nothing else yet
```

The folder name `NNNN-short-name` is the task ID; the numbering (four digits,
leading zeros) determines execution order and sorts correctly up to 9999
tasks. In v0.1 everything is committed, including logs —
maximum observability. If the repo grows too large later, `*.log` can go in
`.gitignore`.

## Task file

`.magnaflow/tasks/0001-short-title/task.md`:

```markdown
---
title: Short title of the task
status: pending        # pending | running | done | failed
branch: task/0001-short-title   # optional: omit = work on the current branch
base: main             # optional: base for the work branch (requires branch:)
group: 001-feature-x   # optional: tasks with the same group share a
                       # Claude session (as long as they directly follow each other)
fresh_session: false   # optional: force a new session
specs:                 # links to relevant spec/knowledge files
  - docs/specs/example.md
attempts: 0
max_attempts: 3
created: 2026-07-02
---

## Goal
What must be finished when this task is done.

## Context
Hints, constraints, relevant files.

## Acceptance criteria
- [ ] Criterion 1
- [ ] Criterion 2
```

No `id:` field in the frontmatter — the folder name is the ID (single source of
truth, can never get out of sync).

### Status model

```text
pending ──> running ──> done
                └─────> failed   (after max_attempts)
```

Deliberately kept small. Statuses such as `proposed`, `accepted`, `reviewing` from
the vision documents come later — they belong to the spec layer, not the
execution layer.

## What the Worker Controller does (v0.1)

```text
mf-worker run <task-id>   # run one specific task
mf-worker next            # pick the first task with status: pending
mf-worker run-all         # work through all pending tasks one by one, then stop
mf-worker status          # show task overview
```

Scanning = read all `tasks/*/task.md`, sort by folder name, take the first with
`status: pending`.

Execution steps of a single task:

1. Read `task.md`; verify `status: pending`.
2. Set status to `running` (frontmatter) and commit this.
3. Create the work branch from the frontmatter (`branch:`, from `base:`).
   Without `branch:` everything runs on the calling branch (branchless),
   with agent work and status transitions as separate commits.
   **Guard:** if the repo is already checked out on a task's work branch,
   the controller refuses (the task list there is a frozen
   snapshot, not a live queue) and points to the
   orchestration branch.
4. Build the prompt: task content + content of the linked `specs:`.
5. Start Claude Code headless (`claude -p`), stream output to
   `claude.log` in the task folder. **Session policy:** multiple prompts within one
   task run in the same session (via `--resume`). If the previous task has
   the same `group:`, that session is reused as well — context is
   then preserved. New group, no group, or `fresh_session: true` =
   new session, so the context does not explode.
6. Run build and test commands from `config.yml`; log to `build.log`
   and `test.log` in the task folder.
7. On failure: feed the error output back to Claude, increment `attempts`,
   repeat until `max_attempts`.
8. Commit the changes on the branch.
9. Write `result.yml` in the task folder (final status, duration, attempts,
   summary) and set the frontmatter status to `done` or `failed`. Commit.

### result.yml

```yaml
task: 0001-project-setup
status: done            # done | failed
attempts: 1
started: 2026-07-02T14:00:00
finished: 2026-07-02T14:12:31
summary: >
  One paragraph: what was built/changed and why.
```

## Deliberately NOT in v0.1

- Dispatcher/poll agent (polling never belongs in the controller itself — separate
  tool later), locking and parallel workers
- Remote/distributed workers
- Dashboard and Runtime Bridge
- Spec Worker (writing specs is still manual / Claude chat work)
- Automatic PRs or merges — the human reviews the branch

## Relationship with GitHub Spec Kit

Two different layers, do not confuse them:

- **We use Spec Kit now** to specify and build the Worker Controller
  itself (`.specify/` in the magnaflow repo).
- **This workflow (`.magnaflow/`)** is what the Worker Controller will
  *consume* in target projects.

The experience with Spec Kit later feeds the design of MagnaFlow's own
spec layer.
