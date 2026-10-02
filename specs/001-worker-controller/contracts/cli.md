# CLI Contract: mf-worker (v0.1)

The command surface is the tool's public API — the future dispatcher composes these commands
and branches on exit codes only (FR-021).

## Global options

| Option | Default | Meaning |
|--------|---------|---------|
| `--project <path>` | current working directory | Target project root (must contain `.magnaflow/`) |

## Commands

### `mf-worker run <task-id>`

Execute one specific task end-to-end (spec User Story 1).

- `<task-id>` = task folder name, e.g. `0001-projectopzet`.
- Preconditions checked in order, before any mutation: project + config valid → environment
  available (`git` and the configured agent executable resolvable; else exit 4) → current
  branch is not any task's work branch (FR-006a; else exit 3 with switch-back instructions) →
  working tree clean (FR-004a) → task exists → `status: pending` (FR-004) → linked specs
  exist (FR-007) → base branch exists when a new work branch is needed (FR-006). A task
  refused on preconditions always stays `pending`.
- Tasks without a `branch:` frontmatter field run branchless: work is committed directly on
  the invoking branch (FR-006).
- Effects: status commits on invoking branch, work commits on task branch, logs +
  `result.yml` in the task folder (see research R6 for the commit choreography).

### `mf-worker next`

Scan `tasks/*/task.md` sorted by folder name, execute the first `pending` task exactly as
`run` would (FR-014). Empty queue ⇒ message + exit 0.

### `mf-worker run-all`

Repeat `next` semantics until no `pending` tasks remain, then stop (FR-015). One task ending
`failed` does not stop the batch. Prints a per-task summary at the end. Never waits for new
work (FR-017).

### `mf-worker status`

Read-only table of all tasks in folder-name order: ID, title, status, attempts/max (FR-016).
Modifies and commits nothing. Malformed task files appear with a warning marker instead of
crashing the overview. When the current branch is a task's work branch, the table is shown
with a frozen-snapshot warning (FR-006a) — status never refuses.

## Exit codes (stable vocabulary — research R7)

| Code | Meaning | Examples |
|------|---------|----------|
| 0 | Requested work succeeded | task `done`; batch all `done`; status printed; nothing pending |
| 1 | Executed but failed | task ended `failed`; run-all with ≥1 `failed` task |
| 2 | Usage / configuration error | unknown task ID, missing/invalid `config.yml`, malformed frontmatter, missing spec file, missing base branch |
| 3 | Precondition refusal | dirty working tree; task not `pending`; invoked from a task's work branch |
| 4 | Environment error | `git` or agent executable not available/authenticated |

Rules: highest-severity applicable code wins; refusals (2/3) mutate nothing. All diagnostics
go to stderr; parseable state lives in the files, never in stdout text (FR-020/021).
