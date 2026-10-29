---
title: "git sync per command, and an agent resolves rebase conflicts"
status: ready
created: 2026-10-29
---

## Context

Queueing the next cmd while the worker is busy is the normal way to
work: the human pushes a `ready` cmd (often with specs ahead and a
`STATUS.md` line) from the desktop while mf-watch runs a command on the
worker machine. Today that is fragile:

- mf-watch pulls once at the start of a poll, runs **every** `ready`
  command back to back, and only pushes at the end. A batch can take
  hours; all that time the two sides drift apart.
- A cmd edited on the remote while it waits in the queue is run as it
  was at the start of the poll.
- When the end-of-poll push is rejected, the rebase-pull replays the
  worker's commits onto the human's. Both sides often touch the same
  files (`docs/specs/STATUS.md`, a spec the running cmd also changes),
  so the rebase conflicts, mf-watch aborts it, notifies, and dispatches
  nothing until a human resolves it by hand. The `resolve-conflicts`
  action (0029) can do that, but only when someone presses Run.

The specs are ahead and are the work order. Read them first:

- `docs/specs/watch/mf-watch.md`: `#config-watch` (`on_conflict`),
  `#one-poll` step 4 (sync per command), `#git-sync` (Auto-resolve),
  `#notifications`.
- `docs/specs/worker/action.md#usage`: `--trigger conflict`.
- `docs/specs/concepts/actions.md#when-an-action-runs`: on a conflict.
- `docs/specs/concepts/worker-run.md`: the Push paragraph.

## Task

1. **Sync after every command** (`Watch/WatchLoop.cs`). With `git_sync`
   on, the dispatch step becomes a loop: take the first `ready` command
   of a fresh scan that has not been dispatched in this poll, run it,
   push (the existing `PushAsync` with its rejection retry), pull, and
   scan again. A failed pull ends dispatch for this poll (the poll is
   then unsynced: no scheduled actions, no final push, as today). Each
   id is dispatched at most once per poll, so a worker that refuses and
   leaves its command `ready` cannot spin. Cancellation still stops
   before the next dispatch. The poll's final push (step 6) stays. With
   `git_sync` off the behaviour is unchanged apart from the rescan.
2. **`on_conflict` config** (`Config/`): a string under `watch:`,
   default `resolve-conflicts`; YAML `null` or an empty string turns it
   off. No other validation.
3. **Auto-resolve** in `WatchLoop.PullAsync`, so it covers all three
   pulls (first, after a command, after a rejected push). On a
   `GitConflictException` whose state differs from `_pullFailureKey`:
   - if `on_conflict` is set and `.magnaflow/actions.yml` loads and
     defines that name: spawn `mf-worker action --project <root> <name>
     --trigger conflict <worker.args...>` the way `RunActionAsync`
     does (same logging, timeout and refusal handling), then pull once
     more;
   - that second pull succeeding is the only success: log `conflict in
     <files> resolved by action <name>`, notify `mf-watch: git conflict
     resolved` with the files, clear the failure state and return as
     a normal pull (HEAD moved → activity);
   - anything else: notify `mf-watch: git pull conflict` as today, the
     message adding `action <name> did not resolve it; see
     .magnaflow/actions/<name>.log`, and remember the state so the next
     poll with the same state neither re-runs the action nor notifies.
   - Without the action (off, no file, invalid file, unknown name) the
     behaviour is exactly today's.
   Never recurse: the second pull does not run the action again.
4. **mf-worker**: accept `--trigger conflict` in
   `Commands/ActionCommand.cs` (validation message and description).
5. **Tests** (xUnit, the existing fakes in `WatchLoopTests` and the
   real-git style of `GitSyncRealGitTests`):
   - two `ready` commands: push and pull happen between them; a command
     made `ready` by the pull after the first run is dispatched in the
     same poll; a refusing worker (status stays `ready`) is dispatched
     once per poll;
   - a pull failure after the first command stops dispatch of the
     second;
   - conflict with `resolve-conflicts` defined: the action is spawned
     with `--trigger conflict`; second pull ok → resolved notification
     and dispatch goes on; second pull conflicting → conflict
     notification naming the log, and the next poll with the same state
     spawns nothing and notifies nothing;
   - `on_conflict: null`, and a project without `actions.yml`: today's
     behaviour;
   - config: default, null, empty string;
   - mf-worker: `--trigger conflict` accepted, an unknown trigger still
     a usage error.
6. **Kit and docs** in the same commit: `tools/mf-watch/README.md` and
   `EXAMPLES.md` where they describe git sync;
   `tools/mf-spec/spec-kit/skills/magnaflow/` (`SKILL.md`: watch syncs
   after each cmd; `actions.md`: the `resolve-conflicts` example now runs
   by itself on a conflict when the watcher's `on_conflict` names it,
   default on). Then run `tools/mf-spec/spec-kit/0002-update-spec-system.md`
   on this repo and rebuild `tools/mf-spec/spec-kit.zip` as the root
   `CLAUDE.md` says. Specs: the ones above are already written; fix them
   where the implementation had to choose, and clear the Spec ahead line
   in `docs/specs/STATUS.md` with a Recent spec updates entry.

## Not in scope

- The worker's own push during a run stays as it is (a rejection is a
  warning); mf-watch is the only side that rebases.
- Branch work (`branch:` cmds), merge commits, force-push.
- Adding `resolve-conflicts` to new projects from the cockpit's Add
  project scaffold.
