---
title: "git sync heals a diverged main: mf-watch rebases, cockpit gets a Sync button"
cmd: 0025-cmd-git-sync-rebase.md
done: 2026-10-06
summary: "mf-watch now pulls with --rebase, retries a rejected push once, and on a conflict or any pull failure dispatches nothing and notifies once per state; the cockpit's Pull becomes Sync (pull --rebase + push) on a diverged copy. Roll out with install.ps1 by hand."
---

# Result

Done. Both test suites pass: mf-watch 73, mf-cockpit 321. `spec_lint` reports 0 problems.

## What changed

**mf-watch**
- `Infrastructure/IGitClient.cs`, `GitClient.cs`:
  - `PullAsync` runs `git pull --rebase --quiet`.
  - If the pull fails while a rebase is in progress (the `rebase-merge` or `rebase-apply` folder from `git rev-parse --git-path` exists), it reads the conflicting files (`git diff --name-only --diff-filter=U`), runs `git rebase --abort` and throws the new `GitConflictException(files)`. That exception is a kind of `GitException`, which is no longer sealed.
  - If the abort itself fails, it throws a plain `GitException` that says so.
  - `PushAsync` now returns `PushOutcome.Pushed` or `RejectedNonFastForward`. "Rejected" means the output has `[rejected]` together with `(fetch first)` or `(non-fast-forward)`. Every other push failure still throws.
  - New `SyncStateAsync()` returns `HEAD..@{u}`, with `?` for any side it cannot read.
- `Watch/WatchLoop.cs`:
  - When the pull fails, the poll still scans and reports stale `running` commands. It then logs `dispatch skipped: ...`, runs nothing and skips the push.
  - Notify-once: the key is the failure kind (`conflict` or `failed`) plus the sync state, kept in `_pullFailureKey` and cleared by a successful pull. A repeat with the same key logs `git pull still failing, state unchanged since the last notice: ...` and does not notify.
  - When the push is rejected as non-fast-forward, the loop logs it, runs one rebase-pull (same notify-once rule) and pushes once more. A second rejection becomes a `git push failed` notification.
  - New notification title: `mf-watch: git pull conflict`.
- Tests:
  - `WatchLoopTests`, using `FakeGitClient`, which gained `PullConflict`, `PushOutcomes` and `SyncState`:
    - the pull uses `--rebase` (real `GitClient` over `FakeProcessRunner`);
    - a rejected push leads to one pull and one retry;
    - a push rejected twice is notified;
    - a conflict skips dispatch and notifies once over two polls, and notifies again when the state changes;
    - a successful pull resets the failure state.
  - The old `GitPullFailureIsNotifiedButPollContinues` is replaced by `GitPullFailureIsNotifiedAndSkipsDispatchAndPush`, because the behavior was meant to flip.
  - New `GitSyncRealGitTests`, against real git with a bare remote, the watcher's clone and a desktop clone:
    - a diverged copy with non-overlapping commits ends up linear on the remote: the desktop commit untouched, the worker's commit on top, no merge commit;
    - a conflicting copy is aborted: HEAD unchanged, clean tree, no rebase folders, nothing dispatched, one notification across two polls.
  - Both clones set `pull.rebase=false`, which proves that the explicit flag wins.

**mf-cockpit**
- `GitInfo` and `ProjectGitInfoDto` gained `Ahead` and `Behind`, both nullable and null by default. They come from `git rev-list --left-right --count HEAD...@{u}`. This costs one more local subprocess inside the existing cached info read.
- `IGitClient.SyncRebaseAsync`: `git pull --rebase` with the network timeout. On a stopped rebase it lists the conflicts and aborts. If the abort fails, the abort's output is appended to the result. On success it calls the existing `PushAsync`, which returns null when there is no remote.
- New endpoint `POST /api/projects/{name}/git/sync`. Its guards are identical to the pull's and in the same order. It returns `GitSyncResponse(Success, ExitCode, Output, TimedOut, Conflicts, Push)`.
- `project.html`:
  - The header shows `↑N ↓M`.
  - New `updatePullControl()` is called from `updateSwitchControls()`, so it runs wherever the dirty, running or git state changes.
  - It labels the button Sync when the copy is both ahead and behind, otherwise Pull.
  - On a dirty tree or a running command it disables the button and sets a `title` with the reason.
  - The click handler posts to `/git/sync` or `/git/pull` and renders the pull line, a conflict line and a push line.
- Tests: `GitSyncApiIntegrationTests`.
  - With the fake: 409 while a command is running, 409 on a dirty tree.
  - Through the real `GitClient` and the real endpoint:
    - a diverged copy reports `ahead=1, behind=1`;
    - Sync rebases, pushes and leaves linear history;
    - a conflict returns 200 with `Conflicts=["shared.md"]`, `CONFLICT` in the output and no push, and HEAD, the tree and the file contents are unchanged.

**Specs and docs**
- `docs/specs/watch/mf-watch.md`: the poll steps, a new `## Git sync` section and the notification table.
- `docs/specs/cockpit/project.md#git`: the ahead/behind header, Pull's disabled `title`, and Sync.
- `docs/specs/concepts/worker-run.md`, the Push paragraph: commits left local are replayed and pushed by `git_sync`.
- `command-lifecycle.md` is unchanged. It does not describe how commits reach the remote.
- Tool docs: the mf-watch README (`git_sync` comment, notifications), mf-watch EXAMPLES §6, and the mf-cockpit README Git bullet.

## Not verified

- The Sync button was not clicked in a real browser. The page script passes `node --check`, and the endpoint is covered end to end against real git. A look at the Git card on a diverged copy is still worth doing at review: after ↻ the label should read Sync, and the result lines should render.
- mf-watch was not run as a live daemon against GitHub. The behavior is covered by real-git tests against a local bare remote.
- Rollout is `tools/install/install.ps1`, by hand, as always. Until then the Linux watcher still runs the bare `git pull`.

## Self-answered questions

- **Push after a failed pull?** No. A poll whose pull failed neither dispatches nor pushes. A diverged push would only be rejected, and its retry would hit the same conflict again. The next successful poll pushes. The cmd only said "dispatch nothing"; this extends that, and it is in the spec.
- **What counts as "the same state"?** The failure kind (conflict or other) plus local `HEAD` and upstream `HEAD` (`@{u}`, as updated by the pull's own fetch). It lives in memory per process, like stale-running, so a restarted watcher notifies once more.
- **Does the scan still run on a failed pull?** Yes. Stale-`running` detection only reads; only dispatch is skipped.
- **Does a rejected push followed by a failing rebase-pull also notify once?** Yes, the same notify-once key applies.
- **How is a rejection told apart from other push failures?** Only `[rejected]` with `(fetch first)` or `(non-fast-forward)` triggers the retry. A hook's `[remote rejected]`, auth or network failures stay plain `git push failed` notifications. Push failures are not deduplicated; the cmd only asked that for pull failures.
- **Dirty tree under `git_sync`:** `pull --rebase` refuses one, where a bare pull sometimes went through. It is treated as an ordinary pull failure: no dispatch, one notification. The worker refuses a dirty tree anyway.
- **Does the server check for divergence before Sync?** No. Sync on a copy that is only behind is a plain fast-forward, so it is harmless. The UI decides the label from ahead/behind.
- **Where do ahead/behind come from?** From the remote-tracking ref as last fetched; the read never fetches. A divergence shows after ↻ or after a refused ff-only Pull (which fetches). This keeps the "only ↻ reaches the network on its own" rule.
- **Pull also disabled with a reason `title`?** Yes. Pull and Sync are one button, and the cmd asks for a `title` that says why before it is pressed. The server's 409s are unchanged. The old Pull title's "(Ctrl+Enter on the message field commits)" hint was dropped, because Commit all carries its own Ctrl+Enter title.
