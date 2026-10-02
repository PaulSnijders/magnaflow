---
title: "mf-cockpit: cheaper page loads — one chrome request, project-only chips, short-lived git/run cache"
status: done
attempts: 1
created: 2026-08-18
---

## Context

Opening `specs.html` — a page that lists markdown files — fires five
requests before it fetches anything of its own: `/api/config`,
`/api/projects`, `/api/projects/{p}`, `/api/projects/{p}/run` and
`/api/projects/{p}/git`. Two of those spawn processes: `/git` runs three
`git` subprocesses, `/run` starts `mf-run status`, which is itself a
fresh .NET process plus a port probe. So four process starts to show a
file list, on every page, again on every live refetch.

The cost comes from `Cockpit.summaryBar()` + `Cockpit.wireNav()`, which
are shared by every page (ontwerp-v0.5.md items 1–2). The bar is worth
keeping; paying for the whole project's state on pages that don't show
it is not.

There is also a layout bug on `specs.html`: the breadcrumb renders as
`docs/specs` on the left, `/` centred and `concepts` hard right.

## Task

1. **Breadcrumb.** `style.css`'s `.card h2 { display: flex; ...
   justify-content: space-between; }` exists for card headers with a
   button group on the right; `specs.html` reuses `.card h2` for a plain
   text breadcrumb, so its three parts get pushed apart. Scope the rule
   to the headers that need it — every one of them contains a direct
   `<span class="row-inline">`, so `.card h2:has(> .row-inline)` is
   enough. Check all four (index Projects, project Run/Git/Watcher).

2. **Branch and run chips only where they belong.** Drop them from the
   shared summary bar; `project.html` already has a full Git card and Run
   card and keeps them. The bar's remaining content (status, latest,
   counts, watcher, live marker, sound toggle) is unchanged everywhere.
   `/git` and `/run` are then requested by `project.html` alone. Add an
   "**Updated:**" note to `docs/fase5-cockpit/ontwerp-v0.5.md` item 2 in
   house style.

3. **One request for the shared chrome.** New `GET /api/overview` with an
   optional `?project=<name>`, returning `{ chatEnabled, projects: [name,
   ...], project: <the same ProjectSummaryDto as /api/projects/{name}, or
   null> }`. `Cockpit.summaryBar` and `wireNav` use it instead of their
   three separate calls; an unknown `?project=` yields `project: null`
   rather than a 404, since the nav must still render. `index.html` keeps
   `/api/projects` for its table and takes `chatEnabled` from the new
   endpoint. Existing endpoints stay as they are — other pages and any
   `curl` habit keep working.

4. **Don't recompute the slow values on every event.** Cache the results
   of `/git` and `/run` per project for ~3 s (a small TTL helper in
   `Infrastructure/`, not a general cache layer) — these are the only two
   endpoints that spawn processes. Invalidate that project's entry
   immediately on its own write actions (commit-all, pull, run
   start/stop/restart), otherwise a button's own status read can show
   stale state. Additionally, give `Cockpit.live`'s `runHandler` an
   in-flight guard so a burst of events cannot stack refetches of a
   handler that is still running.

5. **Tests** in the existing style: the overview endpoint (with project,
   without, unknown name), the TTL helper (hit, expiry, explicit
   invalidation), and the cache cleared by a write action.

## Not in scope

- `git status --untracked-files=all` stays. It is a documented, conscious
  tradeoff in `GitClient.GetInfoAsync` — the changed-file list and the
  commit-message suggestion both break for a new spec file without it.
  Task 4 is what makes it affordable.
- The SSE mechanism itself (hub, watchers, heartbeat, backoff) — unchanged
  here.
