---
title: "mf-cockpit: cheaper page loads — one chrome request, project-only chips, short-lived git/run cache"
cmd: 0010-cmd-cockpit-page-load-cost.md
done: 2026-08-18
summary: Opening a cockpit page costs one chrome request instead of five and no longer spawns any process; project.html keeps its Git and Run cards. Verified live.
---

## What was done

**1. Breadcrumb.** `style.css`'s `.card h2 { display: flex; ...
justify-content: space-between }` is now `.card h2:has(> .row-inline)`,
so only headers that actually carry a button group get pulled apart.
All four button headers still lay out correctly (index Projects,
project Run/Git/Watcher — checked in the browser). Two other headers
were silently affected by the old rule and now read better: see
"Decisions" below.

**2. Branch and run chips.** Gone from `Cockpit.summaryBar` — the
`.sb-run`/`.sb-git` spans, `refreshRun`, `refreshGit`, and the
`{ kind: "run" }` handlers in `specs.html`, `scratchpad.html` and
`project.html`, plus `project.html`'s `bar.refreshGit()` call inside
`loadGit()`. The bar's remaining content (status, latest, counts,
watcher, live marker, sound toggle) is untouched, and `project.html`'s
own Git and Run cards are untouched. `GET .../git` and `GET .../run`
now have exactly one caller each: `project.html` (and `index.html`'s
per-project branch/run columns, which were always their own calls).
`ontwerp-v0.5.md` item 2 gained an `> **Updated (docs/prompts/0010):**`
blockquote in the house style used elsewhere in `docs/fase*/`.

**3. One chrome request.** New `Api/OverviewEndpoints.cs` —
`GET /api/overview?project=<name>` returning
`{ chatEnabled, projects: [name, ...], project: <ProjectSummaryDto|null> }`.
It reuses `ProjectsEndpoints.BuildSummary` (widened from `private` to
`internal`) so the two can never disagree. An absent or unknown
`?project=` yields `project: null` with a 200, never a 404. `wireNav`
and `summaryBar` share one memoized `getOverview(project)` in place of
their three calls, so calling both still costs one request; `refresh()`
still goes to `/api/projects/{name}` for live updates. All existing
endpoints are unchanged.

**4. Not recomputing the slow values.** New
`Infrastructure/TtlCache.cs` — a per-key, 3-second memo over `IClock`,
wired as two DI singletons (`TtlCache<ProjectGitInfoDto>`,
`TtlCache<RunStatusDto>`) in front of the only two endpoints that spawn
processes. `commit-all`, `pull` and every run start/stop/restart
invalidate their own project's entry, so a button's own status read is
never stale. `Cockpit.live`'s `runHandler` now keeps an `inFlight` Set
and drops an event for a handler whose previous refetch is still
running.

**5. Tests.** `TtlCacheTests` (hit within TTL, expiry, per-key
invalidation, invalidating an unknown key, a faulted call not
remembered, concurrent misses sharing one call) and
`OverviewApiIntegrationTests` (overview with/without/unknown project,
run status cached and cleared by a run action, git info cached and
cleared by commit-all, and the memo being per project). `FakeGitClient`
and `FakeRunClient` gained `InfoCalls`/`StatusCalls` counters. 278
mf-cockpit tests pass.

**Verified live** against this repo (throwaway config, port 5399):
`specs.html` now issues one `/api/overview` where it used to issue
`/api/config` + `/api/projects` + `/api/projects/{p}` + `/run` + `/git`
— four process starts down to zero. The breadcrumb renders as one
left-aligned line. Repeated `GET .../git` on this repo: 418 ms, then
2.8 ms and 3.0 ms inside the TTL, then 421 ms again after it expired.
`project.html`'s Git card, lane, and watcher all still work.

## Decisions and deviations from the plan

- **Two other headers changed with the breadcrumb fix.**
  `command.html`'s `<h2 id="cmd-heading">` (title + status pill) and
  `scratchpad.html`'s (title + `· saved 14:32`) also relied on `.card
  h2` being flex, which pushed their second element to the far right of
  the card. Both now sit next to the title. Neither is `.row-inline`, so
  both were caught by the same scoping; both read better for it (the
  scratchpad status literally starts with "· "), so they were left
  fixed rather than special-cased back.
- **`index.html` needed no change for item 3.** It has no chat link and
  never called `/api/config` — it only ever fetched `/api/projects` for
  its table, which it keeps. Nothing to take from the new endpoint.
- **`Cockpit.getConfig` removed.** Its only caller was `wireNav`'s chat
  gating, which now reads `getOverview`; `config.html` calls
  `/api/config` directly and is unaffected. `getOverview` replaces it in
  the exported helper set.
- **The pull guard's `GetInfoAsync` is deliberately uncached.** It is
  what decides whether to touch the working tree at all, and a
  three-second-old "clean" is not good enough to act on. The pull
  invalidates the entry afterwards.
- **`TtlCache` caches the in-flight `Task`, not the value**, so two
  simultaneous readers of one project share a single spawn instead of
  racing; a faulted task is dropped immediately rather than replayed for
  the rest of the TTL. `factory()` runs under the lock on purpose —
  releasing first would re-admit the duplicate spawn being avoided.
- **The in-flight guard is keyed on the handler, as specified.** Note
  that `lane` and `watch` both point at `bar.refresh` on four pages, so
  an SSE connect still fires two identical `/api/projects/{p}` requests.
  Keying on `h.refetch` instead would collapse them, but that dedupes
  *across* handlers rather than guarding one, which is not what was
  asked — and the endpoint involved is the fast, file-I/O-only one.
  Left as an observation.

## Follow-up in the same session (outside this command's scope)

Four tweaks requested directly after this report was written, done in
the same commit:

- **Frontmatter now renders as a key/value block.** `command.html` used
  to hand the whole rst to the markdown renderer, which knows nothing
  about frontmatter — the `--- title: … cmd: … done: … summary: … ---`
  block came out as a run-on paragraph on top of the report (flagged as
  out of scope in 0009's report, and made more visible by it).
  `Cockpit.splitFrontmatter` / `frontmatterHtml` in `app.js` split it
  off and render it as a `dl.lane-frontmatter`; the body goes to the
  markdown renderer as before.
- **The summary moved to the top of the page**, directly under the meta
  line (`id · attempts N`), instead of sitting above the report body —
  the report's own frontmatter block already shows it there.
- **`project.html` card order** is now Run, Lane, New draft command,
  Git, Watcher (was Run, Git, New draft, Lane, Watcher). No design doc
  pinned the old order, so there was nothing to update in the specs —
  `ontwerp-v0.1.md` only lists the page's contents ("the lane as a
  table …, watcher tail, git info"), and already leads with the lane.
- **Tab titles put the distinctive part first.** Every title used to
  begin `mf-cockpit — `, so a strip of truncated tabs read
  `mf-cockpit — ma…` six times over; the app identity is the favicon's
  job. `project.html` now leads with its lane state and keeps it in
  step over SSE — `🟠 running · <project>`, `❓ questions`, `🔴 aborted`,
  `🟢 ready`, or `✅ <project>` when there is nothing to do. Precedence
  mirrors the summary bar's own, and "needs me" reads the server's
  `attention` list rather than raw counts, so one old aborted command
  does not mark the tab red forever (ontwerp-v0.5.md item 5).
  `command.html` shows its own command's status icon; the rest are
  `specs · <project>`, `config · <project>`, and so on.
  `Cockpit.statusIcon` / `projectTitle` in `app.js`, with `summaryBar`
  taking an optional `onSummary` callback so the title needs no second
  fetch. Verified live through all five states against a throwaway
  project.
