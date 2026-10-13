---
title: "Project actions: .magnaflow/actions.yml, mf-worker action, mf-watch schedules, cockpit Actions card"
status: done
created: 2026-10-13
attempts: 1
---

## Context

Projects want named jobs of their own, started from the cockpit or on a
schedule, with a visible log. The driving case is a project script that
syncs a kanban board and writes new items as `ready` cmds. Another is a
prompt that resolves the rebase conflict git_sync gave up on. This is an
optional extension: a project without `.magnaflow/actions.yml` must
behave byte-for-byte as today.

Decision `docs/decisions/0019-actions-run-by-watch.md`: mf-watch decides
*when*, inside its single-threaded poll (so it never overlaps a worker
run, and needs no lock), and spawns `mf-worker action <name>` to run it.
The cockpit shows the actions and writes a request file.

The specs are ahead and are the work order. Read them first:

- `docs/specs/concepts/actions.md`: the file, due rules, guards, files.
- `docs/specs/worker/action.md`: the new subcommand, its order and exit
  codes.
- `docs/specs/watch/mf-watch.md`: `#one-poll` step 5, `#adaptive-backoff`,
  notifications, shutdown.
- `docs/specs/cockpit/project.md#actions` and `#live-updates`.
- `docs/specs/cockpit/index.md#add-project`: one more `.gitignore` line.

## Task

1. **mf-worker `action` subcommand** (`Commands/ActionCommand.cs`, logic
   in a new `Actions/` folder):
   - an `ActionsFile` loader for `.magnaflow/actions.yml` (YamlDotNet,
     as `ProjectConfig`), validating: the name pattern, exactly one of
     `script`/`prompt`, at most one of `every_minutes` (> 0)/`at`
     (`HH:mm`), and `timeout_minutes` > 0, default 30. Each error names
     the action and the field.
   - extensionless `script` resolution as in mf-run (literal, then
     `.cmd`/`.bat`/`.exe` or `.sh`). **Copy** the small resolver; do not
     reference mf-run's assembly (tools never reference each other).
   - the order in `worker/action.md`: guards through the existing
     `PromptScanner` (any `running`) and `GitClient` (dirty tree).
     Scripts run through `IProcessRunner`, prompts through `IAgentRunner`
     with no resume. After a prompt, append `FinalText` to the log.
   - log `.magnaflow/actions/<name>.log` (append, UTF-8 without a BOM, the
     header and result lines from the concept, flushed and closed before
     the post-run dirty check). Write `last.yml` through a temp file plus
     a move, so a concurrent reader never sees half a file.
   - strict argument parsing, as for every other subcommand.
2. **mf-watch** (new `Actions/` folder, own loader; mf-watch reads only
   names and the schedule fields, and still never reads `config.yml`):
   - poll step 5 exactly as in the spec: pending requests first (also
     after a failed pull), then due scheduled actions (only after a
     successful pull or with `git_sync` off), one at a time, before the
     push. A request file is deleted after the spawn. If the delete
     fails because the file is held open (Windows), retry it on the next
     poll, as `WakeFile` does.
   - due rules from `last.yml` `run_at`, through the existing `IClock`.
     The watcher's start time is the baseline for an `at` action that
     has never run.
   - the sleep: `min(backoff interval, until the next due action)`. An
     action refused in this poll is left out of that minimum, so a
     refusal never spins the loop.
   - a refusal (exit 3) is logged once per action and reason (per-process
     memory, as for the git pull notice). It is not notified.
   - exit 1 or any unexpected exit notifies `action <name>: failed`.
   - activity when `HEAD` moved across the actions step.
   - `actions.yml` that does not parse: log `actions.yml: <error>;
     actions skipped` once per file mtime. Never fatal to the loop.
     Requests are then still deleted and logged as skipped.
   - a shutdown request starts no further action.
3. **mf-cockpit**:
   - `Api/ActionsEndpoints.cs`: GET the list (definition, `last.yml`,
     a pending request, a parse error, the `git check-ignore` result),
     GET a log tail (100 lines), POST run. Run accepts only a name that
     exists in the parsed `actions.yml` (404 otherwise; the name becomes
     a file path, so never build that path from an unchecked name).
     409 while no watcher runs (same liveness as the Watcher toggle) or
     while a request is pending. Otherwise write the request file, then
     the wake file if it is not there yet.
   - the Actions card on `project.html` below the Watcher card, as in
     the spec. Hidden without `actions.yml`.
   - `Live/ProjectWatcher.cs`: kind `actions` for `.magnaflow/actions.yml`
     and anything under `.magnaflow/actions/`. Today a nested path counts
     as `evidence`, so check that the new kind does not also raise
     `evidence`.
   - `ProjectScaffolder`: add `!.magnaflow/actions.yml` to the gitignore
     lines.
4. **Dogfood in this repo**: add `!.magnaflow/actions.yml` to the root
   `.gitignore`, and a `.magnaflow/actions.yml` with one manual-only
   action `resolve-conflicts` (prompt: `git pull --rebase`, resolve
   every conflict, build and test per `.magnaflow/config.yml`, finish the
   rebase, `git push`; stop and explain if a conflict is not clearly
   resolvable).
5. **Spec-first**: the specs above are already written. Adjust them only
   where the implementation must deviate, and say why in the rst. Fill
   in the real paths on the `Code:` line of `concepts/actions.md`.

## Tests (xUnit, with fakes for the existing seams)

- worker: loader validation (each rule, defaults); unknown name, exit 2;
  refusal on `running` and on a dirty tree (exit 3, `last.yml` refusal
  written, nothing run, no log section); script ok, failed, timeout;
  prompt sends the text without a resume and logs `FinalText`; a tree
  left dirty is `failed`, exit 1; `last.yml` round-trips.
- mf-watch: no `actions.yml` gives the same spawns as today; a request
  runs after a failed pull while a scheduled action does not; requests
  before scheduled ones, after dispatch, before the push; the request is
  deleted on every outcome; due rules for `every_minutes` and `at`
  (never run, ran before or after today's HH:mm, a missed night catches
  up once); the sleep is cut to the next due time and a refused action
  does not shorten it; a refusal is logged once; HEAD moved means
  activity; an invalid `actions.yml` does not stop the loop.
- cockpit: Run gives 404 for an unknown name and for a path-like name
  (`../x`), 409 without a watcher and 409 while pending; the request and
  wake files are written; the list reports a parse error and an ignored
  file; the event kind maps `actions`; the scaffolder adds the new line.

## Must not change

- Everything for a project without `.magnaflow/actions.yml`: mf-watch
  log lines, spawns, sleep and notifications; the cockpit page and its
  API responses.
- `mf-worker run|next|run-all|status` behavior and exit codes.
- mf-watch never reads `.magnaflow/config.yml`.

## Verify

- `dotnet test` for worker-controller, mf-watch and mf-cockpit.
- After the run, by hand (needs `tools/install/install.ps1`; the worker
  never installs):
  - in a scratch project, add an action `hello` (a script that echoes
    and exits 0) with `every_minutes: 2`. Start the watcher. It runs
    about every 2 minutes, and the card shows the last run and the log.
  - press Run on a manual action while a cmd is being worked: the row
    shows *queued* and runs after the worker finishes.
  - make the tree dirty: the next run shows as refused (dirty tree) on
    the card, with one line in the watcher log.
  - `mf-worker action hello` by hand gives the same log section.
  - in this repo, the card shows `resolve-conflicts` with no ignore
    warning.

## Not in scope

- A `source:` field on action-made cmds plus a lane filter: wait until
  the lane noise is real (agreed in the brainstorm).
- Triggering resolve-conflicts automatically from git_sync's conflict
  notice: button only, by choice.
- Preventing the mid-run commit conflict itself: a separate track, to be
  debugged the next time it happens.
- Editing `actions.yml` in the cockpit's config editor; cron syntax;
  parameters per run; a run history beyond the log.
- Any spec-kit change: actions are a tool feature, not part of the spec
  system.
