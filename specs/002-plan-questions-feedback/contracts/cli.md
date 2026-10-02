# CLI Contract: mf-worker (v0.2)

Amends `specs/001-worker-controller/contracts/cli.md`. Same four subcommands and the same
global option; `pending`/`failed` vocabulary is replaced by `ready`/`aborted`, and every run now
has a plan phase ahead of execution.

## Global options

| Option | Default | Meaning |
|--------|---------|---------|
| `--project <path>` | current working directory | Target project root (must contain `docs/prompts/` and `.magnaflow/`) |

## Commands

### `mf-worker run <cmd-id>`

Execute one specific command end-to-end (spec User Stories 1–3).

- `<cmd-id>` = the `NNNN-name` identity shared by `docs/prompts/NNNN-cmd-name.md` and its optional
  siblings, e.g. `0007-add-export-button`.
- Preconditions checked in order, before any mutation (unchanged from v0.1 except the status
  name): project + config valid → environment available (`git` and the configured agent
  executable resolvable; else exit 4) → current branch is not any command's work branch (else
  exit 3 with switch-back instructions) → working tree clean → command exists → `status: ready`
  (any other status, including `questions`, refuses — exit 3) → linked specs exist → base branch
  exists when a new work branch is needed. A command refused on preconditions is left completely
  untouched.
- **Plan phase** (always first, same run): a separate agent invocation over the cmd body, linked
  specs, and project conventions; writes/updates `NNNN-pln-name.md`.
  - No open question ⇒ continues immediately, same agent session, into the execution phase
    (build/test loop, unchanged from v0.1 mechanics).
  - Open question(s) ⇒ appended to `NNNN-qa-name.md`, status set to `questions`, run ends here —
    exit 0 (pausing for a legitimate decision is normal, successful behavior, not a failure;
    research R8).
- **Resume**: if this command previously paused (a `session.yml` exists under
  `.magnaflow/<cmd-id>/`) and no explicit `resume:`/`group` continuity applies, the controller
  resumes that session by default and re-plans (updates the same pln file) before re-evaluating
  the gate above.
- **Effects on a terminal outcome** (`done`/`aborted`): status commit on the invoking branch, work
  commit on the command's work branch (or the invoking branch, branchless mode — unchanged from
  v0.1), `NNNN-rst-name.md` written/updated in `docs/prompts/`, logs + `session.yml` updated in
  `.magnaflow/<cmd-id>/`.
- **No rst on a `questions` ending**: the pln + qa files already carry full context for that run;
  the report is reserved for runs whose execution phase actually ran.

### `mf-worker next`

Scan `docs/prompts/*-cmd-*.md` sorted by `NNNN`, execute the first `ready` command exactly as
`run` would. Empty queue (no `ready` commands — including any sitting in `questions`) ⇒ message +
exit 0.

### `mf-worker run-all`

Repeat `next` semantics until no `ready` commands remain, then stop. One command ending `aborted`
does not stop the batch. Prints a per-command summary at the end. Never waits for new work.

### `mf-worker status`

Read-only table of all commands in `NNNN` order: ID, title, status, attempts/max. Modifies and
commits nothing. Malformed cmd files (and orphaned pln/qa/rst siblings with no matching cmd file)
appear with a warning marker instead of crashing the overview. When the current branch is a
command's work branch, the table is shown with a frozen-snapshot warning — status never refuses.

## Exit codes (stable vocabulary — unchanged values from v0.1, reinterpreted per research R8)

| Code | Meaning | Examples |
|------|---------|----------|
| 0 | Requested work succeeded, or a legitimate pause occurred | command `done`; command paused at `questions`; batch all `done`/`questions`; status printed; nothing `ready` |
| 1 | Executed but ended `aborted` | retries exhausted; human-recorded abandonment; unrecoverable environment error during a run |
| 2 | Usage / configuration error | unknown cmd ID, missing/invalid `config.yml`, malformed frontmatter, missing spec file, missing base branch, orphaned pln/qa/rst file |
| 3 | Precondition refusal | dirty working tree; command not `ready` (includes `questions`); invoked from a command's work branch |
| 4 | Environment error | `git` or agent executable not available/authenticated; a `questions`-status command's recorded session is no longer resumable |

Rules: unchanged from v0.1 — highest-severity applicable code wins; refusals (2/3) mutate nothing;
all diagnostics go to stderr; parseable state lives in the files, never in stdout text.
