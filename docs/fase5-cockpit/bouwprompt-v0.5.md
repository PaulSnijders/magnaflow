# Build prompt — mf-cockpit v0.5 (quality of life)

A batch of small UX improvements from using the dashboard daily.
Copy-paste the block below into Claude Code, from the repo root.

---

Extend `tools/mf-cockpit/` with the nine items below. Read first:
`docs/fase5-cockpit/ontwerp-v0.1.md` (the invariant and the
write-action list — this fase adds writes #7 and #8 and one
deliberate non-git store), `ontwerp-v0.3.md` (Run card, Config
page, SSE kinds), `ontwerp-v0.4.md` (the `magnaflow.yml` text-append
and its guards — item 4 is its mirror image),
`docs/fase6-mf-run/ontwerp-v0.1.md` (service `url`, PID files),
`docs/fase7-machine-config/completion-notes.md` (config loader +
resolved path), and `docs/fase5-cockpit/watch-toggle-notes.md`.
This fase touches only the cockpit — no changes to mf-worker,
mf-watch or mf-run.

**Step 0.** Write a short `docs/fase5-cockpit/ontwerp-v0.5.md`
first (one or two paragraphs per item, house style), capturing the
decisions below plus anything you have to settle yourself. That is
the spec; then implement. Commit per item — the nine are
independent.

**Frontend rule unchanged:** hand-written HTML/CSS/JS, no build
step, no CDN. Items 1, 2 and 8 add shared behavior — put it in the
existing thin JS helper, not in a framework. If you find yourself
hand-carrying re-render state for a third time on one page, stop
and say so rather than reaching for a library (the agreed threshold
for a Preact+htm migration of *that page*, ESM, no npm).

## 1. Pages refresh themselves

Every page must stay current without F5. The SSE endpoint
`/api/events` already exists (kinds `lane | evidence | watch | run |
projects`); the gap is that not every page subscribes and that a
dropped connection is silent.

- One shared helper: subscribe, filter by project + kind, call a
  page-supplied refetch. Pages **refetch their JSON and re-render**
  — never `location.reload()`.
- Reconnect with backoff. Add a server heartbeat (SSE comment every
  ~15 s); if the client sees none for ~45 s it treats the connection
  as dead and reconnects. This is the Tailscale/laptop-sleep case:
  the socket looks open but is not.
- Refetch immediately on `visibilitychange` → visible, and while
  disconnected fall back to polling (~20 s) so a broken SSE degrades
  instead of freezing.
- Show connection state honestly: a small "live" / "reconnecting…"
  marker with the last-updated time, in the top bar of item 2.
- **Never clobber unsaved input.** The config editor (v0.3) and the
  scratchpad (item 8) must skip the re-render while their textarea
  is dirty and instead show a "changed on disk" notice. Same for
  any open modal.
- Slow data (git card, run card) keeps its own path and cadence —
  do not move it onto the fast summary.

## 2. A summary top bar on the project pages

`project.html` (and, since it is one component, `command.html`,
`config.html`, `specs.html`, `scratchpad.html`) gets a compact
sticky bar under the header answering "is anything happening here?"
in one glance:

- project name + switcher back to index;
- **what is running in the lane**: the `running` command's id +
  short title with the pulsing lava dot, or "idle" plus the latest
  command's id and status;
- counts per status (the same numbers the index row shows);
- watcher alive dot, `run: k/n` services chip, git branch +
  dirty/clean;
- the live/reconnecting marker from item 1.

One row, muted, no card chrome — it must not compete with the page.
Everything in it comes from the fast summary endpoint plus the
existing async run/git calls; it must not add a `git` subprocess to
the fast path.

## 3. Service URLs must use the host you came in on

Opening the dashboard over Tailscale
(`http://<machine>.<tailnet>.ts.net:5210/project.html?p=PiStart`)
and reading `web running (pid 1838134) http://localhost:7658` is
useless — the link points at the browser's own machine.

Rewrite for **display only**: when the rendered URL's host is
`localhost`, `127.0.0.1`, `::1` or `0.0.0.0`, substitute
`window.location.hostname` (and keep scheme, port, path). So the
example becomes `http://<machine>.<tailnet>.ts.net:7658`. Keep
the configured value as the link's `title` so nothing is hidden.
Client-side only: `.magnaflow/config.yml` is not touched, mf-run is
not touched, and viewing from localhost keeps showing localhost.
Apply it everywhere such a URL is rendered (Run card, top bar,
index chips), not just the one spot.

## 4. Remove project — write #7

Bottom of `config.html?p=<name>`, its own clearly separated section
(this is the only destructive-looking control on the page).

**It unregisters, it does not delete.** The working copy, its git
history and its `.magnaflow/` stay untouched on disk; the
confirmation dialog says so in those words, and mentions that
re-adding it later is the "Add project → existing" flow.

`DELETE /api/projects/{name}`:

- remove exactly that entry from `cockpit: projects:` in
  `magnaflow.yml` as a **text edit**, the mirror of write #6 —
  `.bak` first, re-parse and verify the entry is gone and the
  others survived, restore and 500 on any failure, same
  single-writer lock, comments and ordering preserved byte-for-byte
  around the removed lines;
- legacy `mf-cockpit.yml` → refuse, exactly as write #6 does;
- drop the in-memory registration and dispose that project's
  `FileSystemWatcher` (no orphan watcher, no SSE from a project
  that is gone);
- SSE kind `projects`; the browser navigates to `index.html`.

Guards: **409 while any command in that project is `running`** —
unregistering mid-run hides a live worker. If the project's
mf-watch service is alive, do not block, but say so in the dialog
and point at the Watcher card's Stop button — the systemd unit
keeps running after the cockpit forgets the project.

## 5. `aborted` only demands attention when it is the last command

On `index.html`'s attention list, an `aborted` command counts only
when **no command with a higher lane id exists in that project**.
Ordinal sort as always, so `0005 < 0005B < 0006`: writing a
follow-up (or simply moving on to the next number) is the human
saying "seen it". `questions` and stale `running` are unchanged —
they always demand attention.

Unit-test the three cases: aborted as highest id (attention),
aborted with a `0005B` follow-up (no attention), aborted followed
by an unrelated higher number (no attention).

## 6. Index shows the latest lane item

`index.html`'s project table gets a "Latest" column: the highest-id
command's id, truncated title and status pill. Add it to the fast
summary DTO (`GET /api/projects/{name}`) — no `git` subprocess on
that path, per v0.1.

## 7. Git pull — write #8

A Pull button on `project.html`'s Git card, next to "commit all":

- exactly `git pull --ff-only`, nothing else — no merge, no rebase,
  no stash, no autocommit;
- refuse when the tree is dirty, with the message pointing at
  "commit all" (same reasoning as the worker's dirty-tree guard);
- refuse (409) while any command is `running`;
- `GIT_TERMINAL_PROMPT=0` and a hard timeout (~60 s) so a
  credentials prompt fails fast instead of wedging the request;
- exit code plus captured stdout/stderr rendered inline on the card
  — "not possible to fast-forward" is information, not an error to
  swallow;
- on success the lane files change on disk, so the existing
  watcher/SSE already refreshes the page; verify it does.

## 8. Scratchpad page

A per-project notepad: `scratchpad.html?p=<name>`, linked from the
project nav. One textarea, nothing else. Its value is that it lives
on the cockpit host, so the same note is there from every machine
that opens this dashboard.

- **Storage: outside the project repo.** A `scratchpad/` directory
  next to the `magnaflow.yml` the cockpit actually loaded (use the
  loader's resolved path from fase 7), one `<project>.md` per
  project, filename through write #6's name sanitizer. It must
  never land in the working copy: an untracked file there would
  dirty the tree the worker guards. Never committed, never in git,
  by design.
- `GET /api/projects/{name}/scratchpad` → `{content, hash,
  savedAt}`; `PUT` with `{content, baseHash}`, 409 on hash drift,
  256 KB cap, no path parameter at all (nothing to escape).
- Autosave: debounced (~1.5 s idle), plus on blur and on
  `visibilitychange` → hidden, plus explicit Ctrl+S / Ctrl+Enter.
  A small "saved 14:03 · unsaved · saving…" indicator, and on 409 a
  "newer version on the server" choice (reload / keep mine) rather
  than silent loss.
- Invariant note for the design doc: this is the one store the
  cockpit keeps outside git. It is permissible because it is a
  plain text file any editor or `cat` can read and write, and
  because it holds **notes, not system state** — nothing the worker,
  watcher or specs ever read. Say that explicitly in
  `ontwerp-v0.5.md` and in the write-action list update.

## 9. Ctrl+Enter submits

"Create follow up draft" must fire on Ctrl+Enter (Cmd+Enter on
macOS). Then sweep the app for every other primary action behind a
text input and give it the same binding through one shared helper —
at least: create draft (write #1), chat send, the commit-all
message field, config save, the Add-project dialog, the scratchpad.
Where a modal has no Esc-to-close yet, add it. Put the hint in the
button's `title` so it is discoverable.

## Requirements beyond the items

- Unit tests in the existing style: the attention rule (item 5),
  the `magnaflow.yml` entry removal in all the shapes write #6 can
  produce (incl. removing the only entry, and the legacy-file
  refusal), scratchpad path resolution + sanitization + hash
  concurrency, and the URL host rewrite (pure function, test it
  server-agnostically or as a JS unit — your call, say which).
- `WebApplicationFactory` integration tests against temp dirs:
  `DELETE /api/projects/{name}` happy path + the running-command
  409 + a corrupted-append simulation proving the `.bak` restore;
  scratchpad GET/PUT incl. 409 and the cap; pull refused on a dirty
  tree and on a running command (stub `git` the same way existing
  suites stub `chat.command` / `run.command`).
- Live browser verification (`/browse`) as in v0.1–v0.4: watch a
  page update itself with no F5 after a lane change; kill the SSE
  connection and see it reconnect; read a service URL over a
  non-localhost host; remove a test project and land on the index;
  type in the scratchpad, reload, see it; Ctrl+Enter on the
  follow-up dialog. Console clean throughout.
- No behavior change for existing pages and endpoints — run the
  full existing cockpit suite.
- If any of this contradicts the designs above, or an item turns
  out bigger than it reads (item 1 is the likely one), stop and ask
  rather than guess.

When done, write `docs/fase5-cockpit/v0.5-completion-notes.md` in
the same style as `v0.4-completion-notes.md` (what shipped,
validation performed, deviations, left for later) and update
`docs/fase5-cockpit/ontwerp-v0.1.md`'s write-action list with
write #7 (remove project), write #8 (git pull) and the scratchpad's
non-git store.
