---
title: "git sync per command, and an agent resolves rebase conflicts"
cmd: 0031-cmd-sync-per-command.md
done: 2026-10-29
summary: "mf-watch now pushes and pulls after every command and runs the project's resolve-conflicts action on a rebase conflict (on by default, on_conflict: null turns it off). Roll it out with tools/install/install.ps1 on the worker machine."
---

# 0031 — git sync per command, auto-resolve on conflict

## What changed

- `tools/mf-watch/src/MagnaFlow.MfWatch/Watch/WatchLoop.cs`
  - Dispatch is now a loop over fresh scans. It keeps a per-poll `dispatched` id set, and each pass takes the first `ready` command not yet dispatched in this poll.
  - With `git_sync` on, each command is followed by `PushAsync` (which keeps its rejection retry), then `PullAsync`, then a rescan. The rescan also reports stale `running` commands through the existing once-per-process set.
  - If the pull fails, `synced` becomes false. That stops dispatch, skips scheduled actions and skips the final push, and the log says `dispatch stopped: the working copy could not be synced after a command`.
  - `PushAsync` now returns `bool`, which is false only when the rebase-pull after a rejected push failed. In that case the loop skips the extra pull and stops.
  - With `git_sync` off, the only change is the rescan between commands.
- `PullAsync` handles a conflict whose state differs from `_pullFailureKey` like this:
  - **Action available:** `ConflictAction()` returns a name when `OnConflict` is set, `actions.yml` exists, loads, and defines that name. `ResolveConflictAsync` then:
    - logs `...; rebase aborted, running action <name>`;
    - runs the unchanged `RunActionAsync(name, "conflict")`;
    - calls `git.PullAsync()` directly, so the action can never run recursively.
  - **Second pull succeeds:** log `conflict in <files> resolved by action <name>`, notify `mf-watch: git conflict resolved`, clear the key, and return `pulled || HEAD moved`.
  - **Second pull fails:** notify `mf-watch: git pull conflict` with `action <name> did not resolve it; see .magnaflow/actions/<name>.log`, and set the key to the state after the second pull.
- `Config/WatchConfig.cs`: added `OnConflict` (default `resolve-conflicts`, `WatchConfig.DefaultOnConflict`). The DTO setter records whether the key was present, so `on_conflict: null`, `~` and `""` turn it off while a missing key keeps the default. The value is trimmed and whitespace-only counts as off.
- `tools/worker-controller/.../Commands/ActionCommand.cs`: `--trigger conflict` is accepted. The description and the error message are updated.
- Tests:
  - New `WatchLoopSyncTests.cs` (14 tests): sync order, a command readied by a pull, a command taken back on the remote, a refusing worker run once, a failed pull after a command, a failed rebase-pull after a rejected push, the `git_sync` off case, conflict resolved (on the first pull and after a command), unresolved with no retry in the same state, a refused action, and today's behaviour with the action off, without `actions.yml`, and with an unknown name.
  - `FakeGitClient.PullScript` gives scripted per-pull outcomes.
  - Two real-git tests in `GitSyncRealGitTests`: each command reaches the bare remote before the next one runs, and a real conflict is resolved by a fake action, after which the command runs.
  - Config theory: missing key, `null`, `~`, `""`, and a custom name.
  - `CliTests`: triggers manual, schedule and conflict are accepted, and cron is rejected.
  - The two `WatchLoopActionsTests` event sequences now include the push and pull after the command.
- Docs:
  - `tools/mf-watch/README.md`: intro, config block and notifications.
  - `EXAMPLES.md` §6: per-command sync, the sample log, and a paragraph on auto-resolve.
  - Kit `skills/magnaflow/SKILL.md` and `actions.md`: the watcher syncs after each cmd, and `resolve-conflicts` runs by itself through `on_conflict`.
  - Ran `0002-update-spec-system.md`: every installed copy already matched the master except `.claude/skills/magnaflow/`, which was a pure kit copy before this change and is re-copied now. The CLAUDE.md managed section is unchanged. Rebuilt `tools/mf-spec/spec-kit.zip`; the `git add tools/mf-spec/spec-kit` from that recipe left the kit files staged.
- Specs:
  - `watch/mf-watch.md`: three clarifications, listed below.
  - `concepts/actions.md`: the log header lists `conflict` as a trigger.
  - `STATUS.md`: the Spec ahead line is cleared and a Recent spec updates entry is added.
  - `spec_lint` reports 0 problems.

## Notes for the next session

- `RunActionAsync` is reused unchanged. An action that fails (exit 1 or timeout) on a conflict therefore produces two notifications: `action resolve-conflicts: failed`, then `mf-watch: git pull conflict` naming the log. The task asked for "the same ... handling", so this is intended; the spec now says so.
- When the second pull fails without a conflict (for example, the action left the tree dirty), the notification still uses the conflict title and the original files. The remembered key uses kind `failed`, so the next poll's plain pull failure in the same state stays quiet.
- With `git_sync` on, the end of a poll after a command now does push, pull, then the final push, which is usually a no-op. Kept as the task asked.
- The running watcher picks this up only after `tools/install/install.ps1` is run by hand.
- The "this repo" `actions.yml` already defines `resolve-conflicts`, so on the worker machine auto-resolve is on as soon as the new mf-watch is installed.

## Self-answered questions

1. **Which conflict state is remembered after an unresolved attempt?** The state after the second pull (its failure kind plus local and upstream HEAD). If the action committed, the state from before the action would differ from what the next poll sees, and the action would run again on every poll. Added to `mf-watch.md#git-sync`.
2. **What if the second pull fails, but not on a conflict?** It is notified as `mf-watch: git pull conflict` (with the original files and the action log), and the remembered key has kind `failed`.
3. **What counts as activity on a resolve?** The second pull brought commits, or HEAD differs from before the action.
4. **What if the rebase-pull inside a rejected push fails after a command?** That ends dispatch the same way as a failed pull after a command, with no extra pull attempted. Added to step 4 of the spec.
5. **Does a rescan report newly pulled `running` commands as stale?** Yes, through the same once-per-process set as step 3.
6. **What does `on_conflict: ~` or whitespace do?** Both turn it off, like `null` and `""`.
7. **How is `--trigger conflict` accepted in mf-worker tests?** Through `ActionCommand.Settings.Validate()` directly. A CLI-level run would need a real actions file and agent.
