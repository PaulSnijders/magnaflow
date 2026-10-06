---
title: "git sync heals a diverged main: mf-watch rebases, cockpit gets a Sync button"
status: ready
created: 2026-10-06
---

## Context

`main` has two writers now: the worker on the Linux machine (claim,
code and lane commits, pushed by mf-watch's `git_sync`) and the human or
a design session on the desktop. On 2026-10-06 that split `main`:

- the worker committed 0024 locally while the desktop pushed
  `86fe826 docs: …` to GitHub;
- mf-watch's push was rejected, and from then on every poll failed,
  because `GitClient.PullAsync` runs a bare `git pull`, which refuses to
  reconcile divergent branches;
- `PullAsync` failing is only logged and notified: the poll goes on and
  may dispatch a worker on a tree that is behind GitHub, and the
  notification repeats every poll;
- the human fixed it by hand (fetch, rebase the local commit onto
  `origin/main`, push). The two commits touched different files, so
  there was no conflict. That is the normal case.

The cockpit's Pull button cannot help: it runs exactly
`git pull --ff-only`, which refuses a diverged branch.

Read `docs/specs/watch/mf-watch.md`, `docs/specs/cockpit/project.md#git`
and `docs/specs/concepts/worker-run.md` first.

## Task

1. **mf-watch pulls with rebase.** With `git_sync: true`:
   - pull with `git pull --rebase` (explicit flag, so the repo's own
     `pull.rebase` setting does not matter);
   - after the poll's push is rejected as non-fast-forward, run
     `git pull --rebase` and push once more;
   - only local, unpushed commits are ever replayed; nothing that is on
     the remote is rewritten, and there is never a force-push.
2. **A conflict stops dispatch, loudly but once.** If the rebase stops
   on a conflict: `git rebase --abort` (the tree is back where it was),
   log the conflicting files, notify, and dispatch nothing in this poll.
   As long as the same divergence persists (same local and remote
   HEAD), later polls skip dispatch with one log line but do **not**
   notify again; a changed state notifies once more. The same rule
   applies to any other pull failure: no dispatch on a tree that could
   not be synced, and no notification every poll.
3. **Cockpit: Sync button.** In the Git card on `project.html`, when the
   working copy is both ahead of and behind its upstream (the git info
   already knows ahead/behind, or add it), the Pull button becomes
   **Sync**, which runs `git pull --rebase` and then `git push`.
   - Same guards as Pull: refused (409) on a dirty tree or while a
     command is `running`; a `title` says why before it is pressed.
   - On a conflict: `git rebase --abort`, and the conflicting files plus
     git's own output are shown inline. The tree is left as it was.
   - When only behind, Pull stays `--ff-only` as today.
4. **Tests.**
   - mf-watch (fake `IProcessRunner` / git seam as the existing tests use):
     pull uses `--rebase`; a rejected push leads to one rebase-pull and
     one retry; a rebase conflict aborts, skips dispatch and notifies
     once across two polls with the same state.
   - Against real git in a temp dir (like the worker's `GitClientTests`):
     a diverged clone with non-overlapping commits is reconciled by the
     watcher's pull and push; a conflicting one is aborted cleanly.
   - Cockpit: the Sync endpoint's guards (409 dirty / running) and the
     conflict path (abort, tree unchanged, output returned).
5. **Spec-first:** update `docs/specs/watch/mf-watch.md` (git sync:
   rebase, retry, conflict rule), `docs/specs/cockpit/project.md#git`
   (Sync), and `docs/specs/concepts/worker-run.md` or
   `command-lifecycle.md` if they describe how commits reach the remote.

## Not in scope

- Merge commits, force-push, or any change to how the worker itself
  commits.
- Branch work (`branch:` cmds): only the invoking branch the watcher sits
  on is synced, as today.
