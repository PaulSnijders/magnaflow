---
title: "mf-cockpit: switch branch from the Git card"
cmd: 0014-cmd-cockpit-branch-switch.md
done: 2026-09-03
summary: The Git card gained a branch dropdown, a Switch button and a fetch-refresh control, behind GET .../git/branches and POST .../git/checkout; guards and result rendering mirror write #8's pull, and it is written up as the *tenth* write, not the ninth.
---

## What was done

`IGitClient` gained `FetchAsync` (`git fetch --quiet`),
`ListBranchesAsync` (`git for-each-ref --format=%(refname) refs/heads
refs/remotes`, `<remote>/HEAD` dropped, a local branch collapsed with
its remote counterpart, `RemoteOnly` per entry) and `SwitchAsync` (`git
switch <branch>`), with `FakeGitClient` extended to match.

`GET /api/projects/{name}/git/branches` serves that list — one local ref
read, deliberately outside the `TtlCache` — and `?fetch=true` puts one
fetch in front of it. `POST /api/projects/{name}/git/checkout` runs the
switch behind the prompt's guard order: unknown project 404, missing
path 400, branch outside the list 400, running command 409, uncached
`GetInfoAsync` (no git 400, dirty 409 pointing at "Commit all"), then
`SwitchAsync`, then `TtlCache.Invalidate`.

On `project.html` the Git card's `row-inline` now carries a `<select>`,
`Switch` and a `↻` refresh beside `Pull`, plus a `#switch-result` line
rendered like `#pull-result`. `Switch` greys out while the tree is dirty
or a command is running, with the reason in `title`, so it explains
itself before it is pressed. After a successful switch the page warns —
never refuses — about a live watcher and about services still running
from the old tree, both read from state the page already holds, so the
warning costs no request.

Ten integration tests in `GitBranchApiIntegrationTests` mirror
`GitPullApiIntegrationTests` through the `FakeGitClient` seam. Full
suite: 288 passed.

## Deviations from the cmd

- **The numbering.** The cmd calls this "write #9"; number nine in
  `ontwerp-v0.1.md` was already "Check now" (docs/prompts/0008). It is
  written up there as the **tenth** write, with a short note saying so.
- **The "existing two-minute timeout" does not exist.** `GitClient` has
  30s (local) and 60s (network). The fetch uses the network one —
  `PullTimeout` renamed to `NetworkTimeout`, now shared with the pull —
  while the branch list and the switch stay on the local one.
- **`GitPullResult` → `GitCommandResult`.** Pull, fetch and switch
  return an identical shape; one record beat three. Six call sites; the
  `GitPullResponse` wire contract is untouched.
- **The response field is `success`, not `succeeded`**, matching the
  sibling `GitPullResponse` the page already reads, and it also carries
  `timedOut` for the same reason.

## Verified live

A throwaway repo (clean, on `main`, local `werk-fase-1`, `werk-fase-2`
pushed to a bare origin and then deleted locally) registered as a second
cockpit project, driven through the real page:

- the dropdown offered `main`, `werk-fase-1` and `werk-fase-2 (remote)`
  — `origin/HEAD` absent, `origin/main` collapsed into local `main`;
- switching to `werk-fase-2` returned `switch: exit 0 — branch
  'werk-fase-2' set up to track 'origin/werk-fase-2'. / Switched to a
  new branch 'werk-fase-2'`, and the card header plus `index.html`'s
  branch column followed without a page reload;
- on disk, `git branch -vv` shows `werk-fase-2 [origin/werk-fase-2]` —
  a real tracking branch, which is the whole point of `switch` over
  `checkout`;
- dirtying the tree greyed `Switch` out; a `main; rm -rf /` body got a
  400 from the whitelist and a dirty checkout a 409 naming "Commit all";
- `?fetch=true` against this repo's own origin ran a real fetch and
  returned `fetchError: null`; no console errors on the page.

## Note on the debounce (cmd item 5)

`ProjectWatcher.Debounced` is a trailing-edge timer, reset on every
event and fired 500 ms after the last one, so a checkout's burst
collapses into a single SSE event with nothing dropped. No change was
needed. The switch handler refetches the Git card directly anyway — as
the Pull button does — so it never depends on that event arriving.
