# Quickstart Validation: MagnaFlow Worker Controller (v0.1)

Runnable scenarios proving the feature end-to-end. Formats: see
[contracts/file-formats.md](contracts/file-formats.md); commands and exit codes:
[contracts/cli.md](contracts/cli.md).

## Prerequisites

- .NET 10 SDK, git, and Claude Code (`claude --version` works and is authenticated)
- This repo cloned; controller built:

  ```powershell
  cd C:\GIT\magnaflow\tools\worker-controller
  dotnet build; dotnet test
  ```

## Setup: a disposable target project

```powershell
mkdir C:\tmp\mf-demo; cd C:\tmp\mf-demo
git init -b main
dotnet new console -o src; dotnet new xunit -o tests
mkdir .magnaflow\tasks\0001-greet
# config.yml and task file per contracts/file-formats.md:
#   build.command: dotnet build src
#   test.command:  dotnet test tests
#   agent.args:    [--dangerously-skip-permissions]   (opt-in, own machine)
# task 0001-greet: title, status: pending, branch: task/0001-greet,
#   body: make the console app print "Hello MagnaFlow" and add a passing test
git add -A; git commit -m "demo project + task"
```

## Scenario 1 — `run` happy path (User Story 1)

```powershell
mf-worker run 0001-greet
```

Expected:

- Exit code 0; frontmatter ends `status: done`, `attempts: 1`
- Task folder contains `claude.log`, `build.log`, `test.log`, `result.yml` (status `done`)
- `git log main` shows: `running` commit, then terminal commit with logs+result
- Branch `task/0001-greet` exists; its diff vs `main` touches only `src/`/`tests/` (no
  `.magnaflow/` files)

## Scenario 2 — refusals mutate nothing

```powershell
mf-worker run 0001-greet          # already done       → exit 3, no changes
echo x > dirty.txt
mf-worker next                    # dirty tree          → exit 3, no changes
del dirty.txt
mf-worker run 9999-nope           # unknown task        → exit 2
```

Verify `git status` is clean and no task file changed after each refusal.

## Scenario 3 — retry loop and `failed` (bounded attempts)

Add task `0002-impossible` with `max_attempts: 2` and a goal that cannot pass its test (e.g.,
a task whose test command is `exit 1` via a config override in a copy of the project).
Expected: exit 1; `status: failed`; `attempts: 2`; `test.log` shows two attempt headers;
`result.yml` status `failed`.

## Scenario 4 — `next`, `run-all`, `status` (User Stories 2–4)

```powershell
# with 0003-… and 0004-… pending:
mf-worker status     # table: done/failed/pending rows, folder-name order, exit 0
mf-worker next       # executes 0003 only
mf-worker run-all    # executes 0004, then exits (does not wait)
mf-worker next       # "nothing pending", exit 0
```

## Scenario 5 — restartability (SC-004)

Start `mf-worker run 0003-…`, kill the process mid-agent-run. Expected: task file shows
`running` (committed); `mf-worker run 0003-…` refuses with exit 3 and a hint to inspect and
reset; after manually setting `status: pending`, the task runs to completion and history
shows the whole story.

## Success = all five scenarios behave as specified

Maps to SC-001…SC-006 in [spec.md](spec.md). Scenario evidence is reconstructable from files
alone (constitution IV): logs, result files, and git history — no controller output needed.
