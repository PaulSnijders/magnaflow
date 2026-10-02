# File Format Contracts (v0.2)

Amends `specs/001-worker-controller/contracts/file-formats.md`. Full field semantics:
[data-model.md](../data-model.md). `docs/prompts/` is now the bookkeeping surface (was
`.magnaflow/tasks/`); `.magnaflow/` holds only machine runtime evidence.

## `docs/prompts/NNNN-cmd-name.md`

```markdown
---
title: Short command title
status: ready          # draft | ready | running | questions | done | aborted
branch: task/0007-add-export-button   # optional; omit to work directly on the invoking branch
base: main             # optional; default: repository default branch; requires branch:
group: 002-feature-x   # optional; consecutive same-group commands share an agent session
fresh_session: false   # optional; true forces a new session (not combinable with resume)
resume: 0006-earlier   # optional; a command ID (NNNN-name) = continue that command's recorded
                       # session; any other value = a raw agent session ID, passed through
                       # verbatim. Wins over group continuity and the default self-resume.
specs:                 # optional; repo-relative paths included in every phase's prompt
  - docs/specs/example.md
attempts: 0
max_attempts: 3        # optional; default from .magnaflow/config.yml
created: 2026-07-08    # optional; informational
---

## Goal
What must be true when this command is complete.

## Context
Hints, constraints, relevant files.

## Acceptance criteria
- [ ] Criterion 1
- [ ] Criterion 2
```

Controller writes: only the `status:` and `attempts:` values, in place. The body is never written
by the agent (FR-005); the human may also hand-edit `status:` directly (e.g. `draft → ready`,
`questions → ready`, or manually to `aborted`) — the controller is optional automation over this
same file, not the only path to it (FR-022/FR-023).

## `docs/prompts/NNNN-pln-name.md` (plan — executor-owned, only when a question is open)

```markdown
# Plan

<enough of the agent's intended approach to make the question below concrete>

## Open questions (this round)

- <a question genuinely requiring the human — mirrors what was just appended to the qa file>
```

Written **only** when the plan round has at least one open question, or when a pln/qa file for
this command already exists and a previous round's open-questions section has to be dropped —
rewritten **in place** on re-plan, never duplicated. A well-specified command produces no pln at
all: the plan itself is produced in the plan step's reply and stays in the agent session that
implements from it, so the file's presence in the lane is itself the signal that this command
needed a human. The heading `## Open questions (this round)` is the contract between agent and
controller — it is what the controller reads at the gate — so a re-plan that resolved everything
must rewrite the file without it, or the command pauses forever. Questions the agent answered
itself go in the rst, not here.

## `docs/prompts/NNNN-qa-name.md` (question/answer dialogue — created only when needed)

```markdown
## Question (round 1)

Should the export button use the existing `.btn-secondary` style, or a new variant?

**Answer**: <the human writes the answer here, then resets the cmd's status to `ready`>

## Question (round 2)

<a further question, if the re-plan still finds something open — appended below the first>

**Answer**:
```

The executor appends questions; the human writes the answer directly beneath the relevant
question. Further pause rounds append further entries to this same file — never a new qa file.

## `docs/prompts/NNNN-rst-name.md` (report — executor-owned, terminal outcomes only)

```markdown
# Report

## What was done

<prose account of the actual work performed this run, reflecting the final state after retries>

## Decisions taken while implementing

<what was decided or changed along the way, and anything skipped or left uncertain — never a
restatement of the plan>

## Self-answered questions

- Q: <question the command left open> → A: <the answer the executor settled on, and how>

## Abort reason

<only present when status is `aborted`: retries exhausted / human abandonment / unrecoverable
environment error>
```

Written or updated only when a run's execution phase reaches `done` or `aborted` — never for a
run ending `questions` (the pln + qa already carry that context). The self-answered questions
live here, where a human already looks: they are the feedback that says the command was
under-specified.

## `.magnaflow/config.yml`

Extends v0.1: `build`/`test` now each also accept `commands:` (a list, run sequentially,
first failure stops and is the feedback) as an alternative to `command:`. Everything else
unchanged:

```yaml
build:
  command: dotnet build          # required unless commands: is given
test:
  command: dotnet test           # required unless commands: is given
defaults:
  max_attempts: 3                # optional, default 3
  command_timeout_minutes: 30    # optional, default 30
agent:
  command: claude                # optional, default claude
  args: []                       # optional; e.g. [--dangerously-skip-permissions]
```

Monorepo shape, one `commands:` entry per stack:

```yaml
build:
  commands:
    - dotnet build web/Wozzol.sln
    - npm --prefix wozzol-ionic run build
test:
  commands:
    - dotnet test web/Wozzol.Tests/Wozzol.Tests.csproj
```

## `.magnaflow/<NNNN-name>/session.yml`

Replaces v0.1's `result.yml`. Controller-owned; the sole piece of cross-run bookkeeping permitted
in `.magnaflow/`, because it is runtime evidence (an agent session identifier), not content.

```yaml
session: 6a1f0e6e-...   # agent session ID this command last ended with (paused or terminal);
                        # absent if the agent never reported one
```

## Log files (`.magnaflow/<NNNN-name>/`)

`claude.log`, `build.log`, `test.log` — plain text, append-per-invocation with a header line
(`=== plan ===`, `=== attempt 2/3 — 2026-07-08T14:05:00 ===`). Raw subprocess output, no
filtering. Unchanged in spirit from v0.1, now scoped under the command's evidence folder instead
of a `.magnaflow/tasks/<id>/` task folder.

## Project conventions (read-only input, not controller-owned)

Discovered via a fixed order, no configuration:

1. `CLAUDE.md` at the repository root.
2. Else a constitution file: `.specify/memory/constitution.md`, then `docs/constitution.md`
   (first hit wins).

Either or both absent ⇒ silently proceed without them. Whichever is found is prepended, in full,
to every phase's agent prompt (plan and execution).
