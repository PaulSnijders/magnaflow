# mf-cockpit v0.5 — design: quality of life (2026-08-07)

A batch of nine small, independent UX improvements distilled from
using the dashboard daily. Extends `docs/fase5-cockpit/ontwerp-v0.1.md`
(the invariant and the write-action list — this fase adds writes #7
and #8 and one deliberate non-git store), `ontwerp-v0.3.md` (Run
card, Config page, SSE kinds) and `ontwerp-v0.4.md` (the
`magnaflow.yml` text-append — item 4 is its mirror image). The
invariant still governs: every write is a human-initiated click, lands
in an existing plain-text/git format, and could be done by hand. The
one new wrinkle is the scratchpad (item 8), the first store the
cockpit deliberately keeps outside git — permissible for the reasons
spelled out there.

**Frontend rule unchanged:** hand-written HTML/CSS/JS, no build step,
no CDN. Items 1, 2 and 9 add shared behavior — it goes in the existing
thin `Cockpit` helper (`assets/app.js`), not a framework. The agreed
threshold for a Preact+htm migration of a single page (ESM, no npm)
is "hand-carrying re-render state for a third time on one page"; we
are nowhere near it and nothing here changes that.

## 1. Pages refresh themselves

Every page must stay current without F5. The SSE endpoint
`/api/events` already exists (kinds `lane | evidence | watch | run |
projects`); the gaps are that not every page subscribes to it usefully,
and that a dropped connection is silent — the Tailscale/laptop-sleep
case where the socket *looks* open but is dead.

The whole mechanism moves into one shared helper, `Cockpit.live(...)`,
replacing the thin `connectEvents` + `renderLiveBadge` pair. A page
hands it a list of `{kind, refetch}` handlers plus the current project
name; the helper owns the `EventSource`, filters events by project +
kind, and calls the matching page-supplied `refetch`. Pages **refetch
their own JSON and re-render** — never `location.reload()`.

Robustness, all in the helper:

- **Heartbeat.** The server writes an SSE comment (`: ping\n\n`) every
  ~15 s (a new background loop in `EventsEndpoints`, per subscriber).
  The client arms a watchdog: if ~45 s pass with no traffic (event or
  comment) it treats the socket as dead, closes it, and reconnects.
  `EventSource`'s own `onerror` also triggers reconnect. Reconnect
  uses exponential backoff (1 s → 2 s → … capped at ~20 s).
- **Polling fallback.** While disconnected, the helper polls every
  ~20 s — it simply fires every registered `refetch` — so a broken SSE
  degrades to slow-but-working instead of frozen.
- **Visibility.** On `visibilitychange → visible` it refetches
  immediately and, if the socket looks dead, forces a reconnect. Laptop
  wake and tab-focus are the common "why is this stale?" moments.
- **Honest status marker.** The live badge shows `live` /
  `reconnecting…` / `offline (polling)` with the last-updated clock
  time, updated every time any refetch completes. This is the marker
  the item-2 top bar renders.

**Never clobber unsaved input.** A handler may declare itself
*guarded*: `Cockpit.live` skips its `refetch` while a guard predicate
returns true and instead flips a caller-supplied "changed on disk"
notice. The config editor (v0.3) and the scratchpad (item 8) guard on
"textarea is dirty"; the same pattern covers any open `<dialog>` (the
Add-project modal). Slow data (git card, run card) keeps its own
handler and cadence and is never folded into the fast summary refetch.

## 2. A summary top bar on the project pages

`project.html`, and — since they share the same header/nav component —
`command.html`, `config.html`, `specs.html`, `scratchpad.html`, get a
compact sticky bar directly under the header answering "is anything
happening here?" in one glance. It is wired by a shared helper
(`Cockpit.summaryBar`) so all five pages get it identically:

- project name, and a switcher (a `<select>`/link back to the index
  and sibling projects);
- **what is running in the lane**: the `running` command's id + short
  title with the pulsing lava dot, or `idle` plus the latest command's
  id and status;
- counts per status (the same numbers the index row shows);
- watcher-alive dot, `run: k/n` services chip, git branch +
  dirty/clean;
- the live / reconnecting marker from item 1.

One muted row, no card chrome — it must not compete with the page.
Everything except the run chip and the git branch comes from the fast
summary endpoint (`GET /api/projects/{name}`, extended in item 6 with
the latest-command field); the run chip and branch reuse the same
async `GET .../run` and `GET .../git` calls the pages already make, so
the bar adds **no** `git` subprocess to the fast path.

> **Updated (docs/prompts/0010):** the run chip and the git branch are
> gone from the bar. "Reuses the calls the pages already make" was only
> true for `project.html`, which has a full Run card and Git card of its
> own and keeps both; on `specs.html`, `scratchpad.html`, `config.html`
> and `command.html` the bar was the *only* caller, so opening a page
> that lists markdown files spawned three `git` subprocesses and an
> mf-run process — again on every live refetch. The bar now shows only
> what the fast, file-I/O-only summary already knows. Its remaining
> content is unchanged, and it no longer opens a page with three
> requests: `wireNav` + `summaryBar` share one `GET /api/overview`
> (`{chatEnabled, projects, project}`) instead of `/api/config` +
> `/api/projects` + `/api/projects/{name}`. Live refreshes still go to
> the per-project summary endpoint. The two process-spawning endpoints
> are additionally memoized server-side for ~3 s, invalidated by their
> own write actions.

## 3. Service URLs must use the host you came in on

Opening the dashboard over Tailscale and reading
`web running (pid …) http://localhost:7658` is useless — the link
points at the browser's own machine. Fix, **display only**: a pure
function `Cockpit.displayUrl(url)` that, when the rendered URL's host
is `localhost`, `127.0.0.1`, `::1` or `0.0.0.0`, substitutes
`window.location.hostname` while keeping scheme, port and path; any
other host is returned unchanged. The configured value is preserved as
the link's `title` so nothing is hidden. Applied everywhere such a URL
is rendered — the Run card, the item-2 top bar chip, and the index run
chip's tooltip — not just one spot. Nothing server-side changes:
`.magnaflow/config.yml` is untouched, mf-run is untouched, and viewing
from localhost keeps showing localhost (the substitution is a no-op
when `window.location.hostname` is itself `localhost`).

Tested as a JS unit is impractical without a DOM/`window`; instead the
function takes the current hostname as a parameter
(`displayUrl(url, hostname)`) so it is a pure function, exercised by a
tiny standalone test harness note — but per KISS we verify it live
(item requirements) and keep the function trivially inspectable rather
than standing up a JS test runner the repo doesn't otherwise have. The
host-rewrite *rule* is stated here as the spec; the function is the
one-liner mirror of it.

## 4. Remove project — write #7

A clearly separated section at the bottom of `config.html?p=<name>` —
the only destructive-looking control on the page. **It unregisters, it
does not delete.** The working copy, its git history and its
`.magnaflow/` stay untouched on disk; the confirm dialog says exactly
that, and mentions that re-adding later is the "Add project → existing"
flow.

`DELETE /api/projects/{name}`:

- Removes exactly that one entry from `cockpit: projects:` in
  `magnaflow.yml` as a **text edit** — the mirror of write #6.
  `MagnaflowYmlAppender` gains `RemoveProjectAsync` /
  `ComposeRemovedContent`: `.bak` first, apply, re-parse and verify the
  entry is gone *and the others survived*, restore + 500 on any
  failure. Same single-writer lock the appender's callers already hold;
  comments and ordering around the removed lines preserved byte-for-byte
  (removing the only entry leaves an empty `projects:` block — valid,
  and re-addable).
- Legacy `mf-cockpit.yml` → refuse, exactly as write #6 does.
- Drops the in-memory registration (`ProjectRegistry.Remove`) and
  disposes that project's `FileSystemWatcher` — no orphan watcher, no
  SSE from a project that is gone. This requires the watcher host to
  key watchers by project name and expose add/remove;
  `ProjectWatchersHostedService` becomes `IProjectWatcherRegistry`.
- Broadcasts SSE kind `projects`; the browser navigates to
  `index.html`.

Guards:

- **409 while any command in that project is `running`** —
  unregistering mid-run would hide a live worker.
- If the project's mf-watch service is alive, do **not** block, but the
  dialog says so and points at the Watcher card's Stop button: the
  systemd (or Windows) unit keeps running after the cockpit forgets the
  project. This is surfaced from the existing `GET .../watch` the
  config page can call before confirming.

## 5. `aborted` only demands attention when it is the last command

On `index.html`'s attention list, an `aborted` command counts only
when **no command with a higher lane id exists in that project**.
Ordinal sort as always (`0005 < 0005B < 0006`): writing a follow-up, or
simply moving to the next number, is the human saying "seen it".
`questions` and stale `running` are unchanged — they always demand
attention. Implemented in `AttentionRules.Build`: the list is already
ordered by id, so "aborted is the highest id present" is a single
`items.All(other => other.Id <= this.Id)` check over the ordinal
ordering. Unit-tested in three shapes: aborted as the highest id
(attention), aborted with a `0005B` follow-up (no attention), aborted
followed by an unrelated higher number (no attention).

## 6. Index shows the latest lane item

`index.html`'s project table gets a "Latest" column: the highest-id
command's id, truncated title and status pill. Added to the fast
summary DTO (`ProjectSummaryDto.Latest`, populated in
`ProjectsEndpoints.BuildSummary` from the already-scanned lane's last
item) — no `git` subprocess on that path, per v0.1. Null when the lane
is empty. The same field feeds the item-2 top bar's "latest command
when idle" text, so the two never disagree.

## 7. Git pull — write #8

A Pull button on `project.html`'s Git card, next to "commit all":

- exactly `git pull --ff-only`, nothing else — no merge, rebase, stash
  or autocommit;
- refuse when the tree is dirty, with the message pointing at "commit
  all" (same reasoning as the worker's dirty-tree guard);
- refuse (409) while any command in the project is `running`;
- `GIT_TERMINAL_PROMPT=0` and a hard ~60 s timeout so a credentials
  prompt fails fast instead of wedging the request;
- exit code plus captured stdout/stderr rendered inline on the card —
  "not possible to fast-forward" is information, not an error to
  swallow;
- on success the lane files change on disk, so the existing watcher/SSE
  already refreshes the page.

`IGitClient` gains `PullFastForwardAsync(root)` returning
`GitPullResult(ExitCode, Output, TimedOut)`; `GitClient` implements it
via the existing `IProcessRunner` with the env var and timeout.
`GET .../git` is unchanged; a new `POST /api/projects/{name}/git/pull`
runs the guards then the pull. Git stays non-substitutable (no new
config knob — KISS); the endpoint's guards and result-passthrough are
covered through the existing `FakeGitClient` seam, and the real
`git pull --ff-only` invocation (which needs a live upstream) is left
to live verification rather than a fixture remote.

## 8. Scratchpad page

A per-project notepad: `scratchpad.html?p=<name>`, linked from the
project nav. One textarea, nothing else. Its value is that it lives on
the cockpit host, so the same note is there from every machine that
opens this dashboard.

**Storage: outside the project repo, by design.** A `scratchpad/`
directory next to the `magnaflow.yml` the cockpit actually loaded — the
directory of the fase-7 resolved `CockpitConfig.ConfigPath` (even when
that file doesn't exist on disk, i.e. running on defaults, that is the
next-to-binary primary path; decided per the bouwprompt's "settle it
yourself"). One `<project>.md` per project, filename run through write
#6's `DirNameSanitizer` (+ reserved-name guard). It must **never** land
in the working copy: an untracked file there would dirty the very tree
the worker guards. Never committed, never in git.

- `GET /api/projects/{name}/scratchpad` → `{content, hash, savedAt}`.
- `PUT` with `{content, baseHash}`, 409 on hash drift, 256 KB cap
  (mirrors the config PUT). **No path parameter at all** — the filename
  is derived server-side from the project name, so there is nothing to
  escape.
- Autosave: debounced (~1.5 s idle), plus on blur and on
  `visibilitychange → hidden`, plus explicit Ctrl+S / Ctrl+Enter. A
  small `saved 14:03 · unsaved · saving…` indicator; on 409 a "newer
  version on the server" choice (reload / keep mine) rather than silent
  loss.
- Item-1 dirty-guard: the SSE `scratchpad` refetch is skipped while the
  textarea is dirty (shows "changed on disk" instead). A new SSE kind
  is **not** needed — the file lives outside any watched directory, so
  no `FileSystemWatcher` fires for it; concurrency is handled entirely
  by the hash check on PUT. (Cross-machine live updates of the same
  note are out of scope: the hash-409 is the safety net.)

**Invariant note.** This is the one store the cockpit keeps outside
git. It is permissible because (a) it is a plain text file any editor
or `cat` can read and write, and (b) it holds **notes, not system
state** — nothing the worker, watcher or specs ever read. It is
deliberately outside the working copy precisely so it cannot dirty the
tree. See the write-action list update in `ontwerp-v0.1.md`.

## 9. Ctrl+Enter submits

A shared helper `Cockpit.submitOnCtrlEnter(textarea, submit)` fires a
primary action on Ctrl+Enter (Cmd+Enter on macOS) from inside a text
input, and puts the hint in the button's `title` for discoverability.
Swept across every primary action behind a text input: create draft
(write #1), the follow-up draft (write #3), chat send, the commit-all
message field, config save, the Add-project dialog, and the scratchpad.
Where a `<dialog>` lacks Esc-to-close it gets it (the native `<dialog>`
already closes on Esc; the Add-project modal is a native `<dialog>`, so
this is mostly already covered — verified, not assumed).

## Write-action list (delta)

Adds to `ontwerp-v0.1.md`'s list:

- **write #7 — remove project** (`DELETE /api/projects/{name}`): a
  narrow text edit removing one entry from `cockpit: projects:` in
  `magnaflow.yml`, the mirror of write #6, with the same `.bak` +
  reparse-verify + restore guards. Unregisters only; never deletes the
  working copy or its git history.
- **write #8 — git pull** (`POST /api/projects/{name}/git/pull`):
  exactly `git pull --ff-only`, refused on a dirty tree or a running
  command, output rendered inline. Lands in git the normal way (it *is*
  git).
- **the scratchpad's non-git store**: a `scratchpad/<project>.md` next
  to the loaded `magnaflow.yml`, outside every working copy. The single
  deliberate exception to "no state that exists only inside the
  cockpit" — permissible because it is plain text holding notes, not
  system state, that no other tool ever reads.

## Not in scope (v0.5)

Cross-machine live sync of a scratchpad (hash-409 is the guard); a
rich-text notepad (one textarea, KISS); undo for remove-project beyond
re-adding as "existing"; a JS unit-test runner for the pure frontend
helpers (item 3's rule is stated here and verified live); auth
(VPN/LAN is still the perimeter).
