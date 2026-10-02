# File Format Contracts (v0.1)

These files are the interface between target projects and every current/future MagnaFlow tool
(constitution II/VII). Full field semantics: see [data-model.md](../data-model.md).

## `.magnaflow/tasks/NNNN-short-name/task.md`

```markdown
---
title: Short task title
status: pending        # pending | running | done | failed
branch: task/0001-short-title   # optional; omit to work directly on the invoking branch
base: main             # optional; default: repository default branch; requires branch:
group: 001-feature-x   # optional; consecutive same-group tasks share an agent session
fresh_session: false   # optional; true forces a new session (not combinable with resume:)
resume: 0001-earlier   # optional; a task ID (NNNN-name) = continue that task's recorded
                       # session (result.yml `session:`); any other value = a raw agent
                       # session ID, passed through verbatim. Wins over group continuity.
specs:                 # optional; repo-relative paths included in the prompt
  - docs/specs/example.md
attempts: 0
max_attempts: 3        # optional; default from config.yml
created: 2026-07-02    # optional; informational
---

## Goal
What must be true when this task is complete.

## Context
Hints, constraints, relevant files.

## Acceptance criteria
- [ ] Criterion 1
- [ ] Criterion 2
```

Controller writes: only the `status:` value and the `attempts:` value, in place. No other
line is ever touched by tooling.

## `.magnaflow/config.yml`

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

`build` and `test` each accept `command:` (a single string, as above) or `commands:` (a
list of strings, run sequentially — the first failure stops the sequence and its output is
the feedback). Exactly one of the two is required per section; a monorepo with two stacks
gets one `commands:` entry per stack:

```yaml
build:
  commands:
    - dotnet build web/Wozzol.sln
    - npm --prefix wozzol-ionic run build
test:
  commands:
    - dotnet test web/Wozzol.Tests/Wozzol.Tests.csproj
```

## `.magnaflow/tasks/<id>/result.yml`

```yaml
task: 0001-project-setup
status: done            # done | failed
attempts: 1
started: 2026-07-02T14:00:00
finished: 2026-07-02T14:12:31
session: 6a1f0e6e-...   # agent session ID the run ended with (absent if the agent
                        # never reported one); target of other tasks' `resume:`
summary: >
  One paragraph: what was built/changed and why.
```

Controller-owned; overwritten on every completed run of the task.

## Log files (per task folder)

`claude.log`, `build.log`, `test.log` — plain text, append-per-attempt with an attempt header
line (`=== attempt 2/3 — 2026-07-02T14:05:00 ===`). Raw subprocess output, no filtering.
