---
title: "mf-cockpit: switch branch from the Git card (write #9)"
status: done
created: 2026-09-03
---

## Context

Working on a per-fase branch (`werk-fase-1`, merge, then
`werk-fase-2`) needs no code change. `TaskRunner` runs on whatever
`invokingBranch` reports and `main` is hardcoded nowhere;
`GetDefaultBranchAsync()` only feeds error messages and the default
`base:` for a cmd that carries its own `branch:`. mf-watch's
`IGitClient` states it outright: working copy, branch and remote are
"whatever the human already set up on this machine".

The price is a manual step per machine, and it cannot be otherwise:
bookkeeping commits on the invoking branch, so the lane is *branch*
state, not project state. A machine left on `main` pulls `main`, finds
no ready commands and says nothing — a silent failure mode, not an
error.

On the desktop that step is free: you are in the terminal to merge
anyway. On the Linux worker it is not — there is no terminal in that
flow, but the cockpit is reachable over Tailscale. That machine is the
whole reason for this command. It also decides two details below: the
branch you want to switch to exists there only as `origin/<name>`
until something fetches, and it must become a tracking branch, because
mf-watch runs bare `git pull` / `git push` with no upstream arguments.

The branch indicator already exists — the Git card header on
`project.html`, and a column on `index.html` with `*` for dirty.
Nothing to add there.

## Task

1. **`IGitClient` gains three calls**, alongside `PullFastForwardAsync`
   and in the same style (`RunAsync` in the project root, the existing
   two-minute timeout, result passed back rather than interpreted):

   - `FetchAsync(projectRoot)` — `git fetch --quiet`.
   - `ListBranchesAsync(projectRoot)` — local branches plus
     remote-tracking refs, `origin/HEAD` excluded, a local branch and
     its remote counterpart collapsed into one entry. Returns the
     current branch and, per entry, whether it exists *only* on a
     remote.
   - `SwitchAsync(projectRoot, branch)` — `git switch <branch>`, not
     `git checkout`. For a remote-only name `switch` creates the local
     tracking branch by itself, which is exactly the worker case; a
     bare `checkout` would fail there.

   Extend `FakeGitClient` to match.

2. **`GET /api/projects/{name}/git/branches`**, optional
   `?fetch=true`. Without the flag it is one local ref read — do not
   put it in the `TtlCache`; what 0010 made expensive was
   `git status --untracked-files=all` and `git log`, and this is
   neither. With the flag it runs `FetchAsync` first. The fetch is a
   network call and stays an explicit human action: never on page load
   and never on opening the dropdown.

3. **`POST /api/projects/{name}/git/checkout`**, body `{ branch }`,
   guards in this order — the same shape and the same reasoning as
   write #8:

   1. unknown project → 404; path missing on disk → 400.
   2. `branch` is not in the list from step 2 → 400. The list is the
      whitelist; no caller-supplied text ever reaches a git argument.
   3. `ProjectResolver.HasRunningCommand` → 409. A checkout mid-run
      swaps the tree under the agent.
   4. `GetInfoAsync`, **uncached** for the same reason pull reads it
      uncached: a few-seconds-old "clean" is not good enough to act
      on. Not available → 400; dirty → 409, pointing at "Commit all".

   Then `SwitchAsync`, invalidate that project's `TtlCache` entry (as
   pull does, so the card's own refetch is never stale), and return
   `{ succeeded, exitCode, output, branch }` for inline rendering.
   Failure output goes back verbatim — including git's own refusal
   when a remote-only name is ambiguous across several remotes, which
   needs no handling of its own.

4. **Git card UI.** A `<select>` plus a `Switch` button in the
   `row-inline` that already holds `Pull`, and a small refresh control
   next to it that reloads the list with `?fetch=true`. Populate on
   load without the flag. Result line rendered like `#pull-result`.

   Disable `Switch` while the tree is dirty or a command is running,
   with the reason in `title` — the button explains itself before it
   is pressed, the way "Commit all" already does.

   After a successful switch, warn rather than refuse, from state the
   page already holds (this must not add a request):

   - watcher alive → it may start a queued command on this branch at
     any moment. There is no way to detect a poll in flight —
     `.magnaflow/mf-watch.lock` is a process-lifetime instance lock,
     not a per-poll one — and refusing while a watcher runs would
     disable the button on the one machine it exists for. Guard 3
     already covers the run that is actually in progress.
   - services running → they are still the old tree's processes; the
     Run card and its Restart button are on the same page.

5. **Refresh.** A checkout rewrites `.magnaflow/` and `docs/prompts/`,
   so the existing FileSystemWatcher fires and SSE refreshes the page.
   Check that the debounce collapses that burst rather than dropping
   the last event, and refetch git info and the branch list right
   after the click, as the pull button does.

6. **Design doc.** Add write #9 to the write list in
   `docs/decisions/0007-cockpit-design.md` in house style, with an
   `> **Updated (docs/prompts/0014):**` note. No new design doc.

7. **Tests.** Mirror `GitPullApiIntegrationTests` through the
   `FakeGitClient` seam: 404 on an unknown project, 400 on a branch
   outside the list, 409 while a command is running, 409 on a dirty
   tree, and the pass-through of a successful and a failing switch.
   The real `git switch` against a live remote is left to live
   verification, same note as write #8.

## Verify live

On a project with a second branch: switch, and see the Git card header
and the `index.html` column follow without a page reload. Dirty the
tree — `Switch` goes disabled with a reason. Push a new branch from
another machine: it appears in the dropdown only after the refresh
control is used, and switching to it produces a local branch with an
upstream (`git status` says "up to date with origin/<name>", not "no
upstream").

## Not in scope

Creating, merging, pushing or deleting branches; checking out a tag,
a commit or anything that would leave a detached HEAD (the worker
throws on that by design); switching from any page other than
`project.html`; and any cross-machine awareness — a cockpit knows only
its own working copy.
