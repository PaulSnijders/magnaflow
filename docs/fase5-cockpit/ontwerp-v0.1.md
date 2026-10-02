# mf-cockpit v0.1 — design (2026-07-13)

The "glasbox" dashboard from the phase-1 brainstorm and
`docs/kennis/cockpit.md`, now concrete: a small web app that renders
the plain-text state of one or more MagnaFlow projects, plus an AI
chat that answers questions about a project and can feed the existing
prompt lane. It is a **window** on the system, never a second actor
in it.

```text
files on disk (lane, evidence, specs, logs)
      → mf-cockpit reads and renders them        ← the window
      → human watches, drills down, asks the chat
      → chat answers read-only, or drafts a cmd file
      → mf-watch picks the cmd up via the normal lane
```

## Invariant: buttons, not an actor

Everything the cockpit shows must remain readable without it (`cat`
suffices), and every action it offers must be something a human
could also do by hand at the file/git level — same rule as the
worker and mf-watch. Buttons are expected to grow over versions
(commit, branch, retry, …); the invariant is not "read-only" but:

- every write is an explicit **human-initiated** action (a click),
  never a decision the cockpit takes on its own;
- every write lands in the existing plain-text/git formats — no
  state that exists only inside the cockpit;
- the cockpit never touches `.magnaflow/` (worker evidence) and
  never edits specs.

> **Re-scoped (v0.3):** what the "never touches `.magnaflow/`" clause
> protects is the **run evidence** (`.magnaflow/<id>/`, the worker's
> own diary) — it keeps meaning exactly that. `.magnaflow/config.yml`
> (write #5, below) and `.magnaflow/run/` (read-only display) are not
> evidence and are touched, each within the existing rules. See
> `docs/fase5-cockpit/ontwerp-v0.3.md`'s "Invariant clarification".

In **v0.1** the action set is deliberately just two, both inside
`docs/prompts/`:

1. Create a new `NNNN-cmd-name.md` with `status: draft` (from the
   chat or a form).
2. Flip exactly `status: draft` → `status: ready` on a cmd file (the
   human's "go" button).

**v0.2** adds a third, same rules: create a **follow-up** draft
(`0005-fix-lava` → `0005B-cmd-<slug>.md` → `0005C-...`, and so on —
the letter is a human-facing convention only, not machine-parsed
meaning) from a terminal (`done`/`aborted`) command's "continue on
this command" button. It copies `branch`/`base`/`group`/`specs` from
the parent and sets `resume:` to the parent's own recorded session id
(`.magnaflow/<parent-id>/session.yml`) so the worker continues the
same Claude session — a parent with no recorded session still gets a
draft, just without `resume:`, with a warning surfaced to the human.

A fourth write (post-v0.2): **"commit all"**, a button on
`project.html`'s Git card that runs exactly `git add -A && git commit
-m "<message>"` over whatever is currently pending — hand-edits
included, the exact "git commit of hand-edits" action anticipated but
deferred in v0.1's "Not in scope" list. The default commit message is
the name of the one spec file among the pending changes, if there is
exactly one (mf-spec's own spec-first rule means a behavior change
and its owning spec commonly land together); zero or several spec
files is genuinely ambiguous, so no default — the human types their
own.

A fifth write (v0.3): editing exactly `.magnaflow/config.yml` — a
raw-YAML save (hash-checked, YAML-validated) on `config.html`,
committed the same way (`cockpit: edit config`). See
`docs/fase5-cockpit/ontwerp-v0.3.md`'s "The Config page" for the
save flow and guards; the re-scoping above is what makes this write
permissible under the "never touches `.magnaflow/`" clause.

A sixth write (v0.4): "Add project" — a `+` on `index.html` that
either registers an existing working copy or scaffolds a brand-new
one (git repo, spec kit, optional code template) and seeds the
first spec-kit command as a draft. `POST /api/projects`, `mode:
"new"|"existing"`. Registering also appends one entry to `cockpit:
projects:` in `magnaflow.yml` — a narrow, structured text edit, not
an editor, and the one deliberate exception to "the cockpit never
touches machine config" (see `docs/fase5-cockpit/ontwerp-v0.4.md`'s
"Invariant note (write #6, and the machine config)" for why this
differs in kind from the still-out-of-scope raw machine-config
editor). See `docs/fase5-cockpit/ontwerp-v0.4.md` for the dialog,
the `cockpit.new_project` config shape, the scaffold pipeline, and
the `magnaflow.yml` append's own guards (`.bak` + reparse-verify,
single-writer lock).

A seventh write (v0.5): "Remove project" — a `DELETE
/api/projects/{name}` behind a clearly separated control at the
bottom of `config.html`. It **unregisters, it does not delete**: the
mirror of write #6, removing exactly one entry from `cockpit:
projects:` in `magnaflow.yml` as a byte-for-byte text edit (same
`.bak` + reparse-verify + restore + single-writer-lock guards, now
also verifying the *other* entries survived), plus dropping the
in-memory registration and that project's `FileSystemWatcher`. The
working copy, its git history and its `.magnaflow/` are never
touched. Refused (409) while any command in the project is
`running`; the legacy `mf-cockpit.yml` filename is refused, exactly
as write #6 does. See `docs/fase5-cockpit/ontwerp-v0.5.md` item 4.

An eighth write (v0.5): "Git pull" — a `POST
/api/projects/{name}/git/pull` button on `project.html`'s Git card
running exactly `git pull --ff-only` (no merge/rebase/stash/
autocommit). Refused on a dirty tree (pointing at "commit all") and
while any command is `running`; `GIT_TERMINAL_PROMPT=0` + a hard
timeout so a credentials prompt fails fast; git's exit code and
captured output rendered inline. This write *is* git, so it lands in
git the normal way — no extra commit of the cockpit's own. See
`docs/fase5-cockpit/ontwerp-v0.5.md` item 7.

A ninth write (docs/prompts/0008): "Check now" — a `POST
/api/projects/{name}/watch/check-now` button on the Watcher card that
creates one empty file, `.magnaflow/mf-watch.wake`, asking a running
mf-watch to poll now instead of finishing its backed-off sleep (see
`docs/fase4-mf-watch/ontwerp-v0.1.md`'s "Adaptive polling"). Not run
evidence, so within the re-scoped `.magnaflow/` clause above; not
committed either, since it is a transient signal mf-watch deletes on
sight rather than state — `touch` is the by-hand equivalent. It is
deliberately *not* on `IWatchControl`: creating a file is identical on
systemd and Windows, so this one needs no platform split.

> **Updated (docs/prompts/0012):** the button is not on the Watcher card
> any more. It is the most-pressed control on the page and that card is
> the last one, so it moved to a plain action row directly under the
> summary bar, above the first card. Endpoint, disabled-while-stopped
> rule and post-click refetch are unchanged; Start/Stop stayed in the
> card.

A tenth write (docs/prompts/0014): "Switch branch" — a `<select>` and a
`Switch` button beside `Pull` on `project.html`'s Git card, running
exactly `git switch <branch>` behind `POST
/api/projects/{name}/git/checkout`. The lane is *branch* state, not
project state — the worker's bookkeeping commits land on the invoking
branch — so working a fase per branch (`werk-fase-1`, merge, then
`werk-fase-2`) costs one checkout per machine and nothing else in the
stack changes. On the desktop that step is free, since you are in the
terminal to merge anyway; on the Linux worker there is no terminal in
that flow, only the cockpit over Tailscale, and that machine is the
whole reason this write exists. A machine left behind on the old branch
pulls the old branch, finds no ready commands and says nothing — a
silent failure mode, not an error, which is what makes the control worth
a button.

The list `GET /api/projects/{name}/git/branches` hands out is also the
*whitelist* the checkout validates against, so no caller-supplied text
ever reaches a git argument. It is local branches plus remote-tracking
refs with `<remote>/HEAD` dropped and each local branch collapsed with
its remote counterpart; one local ref read, so deliberately not in the
`TtlCache` (what docs/prompts/0010 made expensive was `git status
--untracked-files=all` and `git log`, and this is neither). `?fetch=true`
puts one `git fetch` in front of it and is the only thing in the flow
that touches the network — bound to the card's own refresh control,
never to a page load or to opening the dropdown. `git switch`, not `git
checkout`: for a name that exists here only as `origin/<name>` — the
worker case — `switch` creates the local tracking branch by itself,
where a bare `checkout` would fail, and it cannot silently detach HEAD.

Guards, in the pull's order and for the pull's reasons: unknown project
404, the branch whitelist 400, refused (409) while any command is
`running` (a checkout would swap the tree under the agent), refused
(409) on a dirty tree, pointing at "Commit all". The dirty read is
uncached for the same reason the pull's is — a few-seconds-old "clean"
is not good enough to act on — and both refusals are also rendered ahead
of time in the button's `title`, so it explains itself before it is
pressed. Git's own exit code and output are passed back verbatim, an
ambiguous remote-only name included. Afterwards the page *warns* rather
than refuses, out of state it already holds: a live watcher may start a
queued command on the new branch at any moment (a poll in flight cannot
be detected — `.magnaflow/mf-watch.lock` is a process-lifetime lock, not
a per-poll one — and refusing while a watcher runs would disable the
button on the one machine it exists for), and any running services are
still the old tree's processes. Like write #8 this one *is* git, so it
lands in git the normal way — no extra commit of the cockpit's own.

> **Note on numbering (docs/prompts/0014):** the prompt calls this
> "write #9". Number nine was already taken by "Check now" above, so it
> is written up here as the tenth; the endpoints and behaviour are the
> prompt's.

All the git-landing writes above are committed immediately (`git add <file> && git
commit -m "cockpit: ..."`, or `git add -A` for "commit all"), because
the worker's dirty-tree guard is right to refuse uncommitted lane
files, and on a `git_sync` machine mf-watch's next push is the
outbox. No other file, no other status transition, ever — "commit
all" is the one deliberate exception to "no other file", since its
entire point is to commit whatever else is already sitting on disk.
Write #6's own scaffold commit (`cockpit: create project <name>`,
inside the *new* project's own repo) follows this same rule; its
registration half (the `magnaflow.yml` append) is the one write that
cannot be, since `magnaflow.yml` itself is not in git — see
`docs/fase5-cockpit/ontwerp-v0.4.md`'s "Invariant note" for why that
is still permissible.

One deliberate exception to "no state that exists only inside the
cockpit" (v0.5): the **scratchpad** — a per-project `scratchpad/
<project>.md` next to the loaded `magnaflow.yml`, kept **outside every
working copy and outside git by design**. It is permissible because it
is a plain text file any editor or `cat` can read and write, and
because it holds **notes, not system state** — nothing the worker,
watcher or specs ever read. It lives outside the working copy
precisely so it can never dirty the tree the worker guards. See
`docs/fase5-cockpit/ontwerp-v0.5.md` item 8.

## What it shows

Multi-project from day one: `projects:` in config is a list of
`{name, path}` working copies. Data sources per project — all
existing formats, nothing new invented:

- **Lane** — `docs/prompts/NNNN[B-Z]-{cmd,pln,qa,rst}-name.md`
  (the optional letter is a v0.2 follow-up, e.g. `0005B`), grouped by
  `NNNN[B-Z]-name` id — plain ordinal sort places a follow-up right
  after its parent and before the next number, no extra logic needed.
  Cmd frontmatter: `status` (`draft | ready | running | questions |
  done | aborted`), `title`, `attempts`, `max_attempts`, `branch`,
  `base`, `group`, `specs`, `created`, `resume`.
- **Evidence** — `.magnaflow/<NNNN-name>/`: `claude.log`,
  `build.log`, `test.log`, `session.yml` (tail views; these can be
  large).
- **Watcher** — `.magnaflow/mf-watch.log` (tail),
  `.magnaflow/mf-watch.lock` presence = "watcher alive on this copy".
- **Specs** — `docs/specs/`: `STATUS.md`, `_overview.md`, and the
  spec tree (rendered markdown, read-only browse).
- **Project config** — `.magnaflow/config.yml` (display only).
- **Git** — current branch, last N commits, dirty/clean
  (`git status --porcelain`, `git log` — read-only shell-outs).

## Pages

Small static pages sharing one CSS file and a thin JS helper; no SPA
framework. Server renders nothing — every page fetches JSON from the
API. Splitting pages is free because state lives server-side in
files, not in the page.

- `index.html` — the cockpit: one row per project with status counts
  and an **attention list** (anything `questions`, `aborted`, or
  stale `running`, across all projects). This answers "who is at
  bat?" in one glance — the exact gap `docs/kennis/cockpit.md`
  records.
- `project.html?p=<name>` — the lane as a table (id, title, status,
  attempts, branch), watcher tail, git info, link to specs.
- `command.html?p=<name>&id=<id>` — one command's whole story: cmd
  rendered, pln, qa, rst side by side, evidence log tails. The
  drill-down for "what happened here?".
- `chat.html?p=<name>` — the AI chat (below).
- `specs.html?p=<name>` — spec tree browse + rendered file view.

## API

`GET /api/projects` · `GET /api/projects/{name}` (fast: counts +
attention + watcher only — no `git` subprocess on this path at all) ·
`GET /api/projects/{name}/git` (slow half — branch, dirty, last N
commits, default commit message; at least one `git` subprocess spawn,
seconds on a repo with a large untracked tree — split into its own
request specifically so it never delays the fast summary above) ·
`GET /api/projects/{name}/commands` ·
`GET /api/projects/{name}/commands/{id}` (all lane files + evidence
tails) · `GET /api/projects/{name}/watch?tail=200` ·
`GET /api/projects/{name}/specs[/{**path}]` ·
`GET /api/events` (SSE) ·
`POST /api/projects/{name}/commands` (create draft — write #1) ·
`POST /api/projects/{name}/commands/{id}/ready` (draft→ready — write
#2) · `POST /api/projects/{name}/commands/{id}/follow-up` (follow-up
draft — write #3) · `POST /api/projects/{name}/git/commit-all`
("commit all" — write #4) · `GET /api/projects/{name}/git/branches`
(`?fetch=true` to fetch first — write #10) · `POST
/api/projects/{name}/git/checkout` (switch branch — write #10) ·
`POST /api/projects/{name}/chat` (stream reply).

## Live updates

One `FileSystemWatcher` per project on `docs/prompts/` and
`.magnaflow/`, debounced (~500 ms), fanned out over a single SSE
endpoint (`/api/events`, event = project name + coarse kind:
`lane | evidence | watch`). Clients re-fetch what they show; no
payloads over SSE. Where mf-watch deliberately polls (one code path
everywhere), the cockpit may watch: it runs on one machine over local
paths, and a missed event costs only a stale page until the next
change or manual reload — no correctness at stake.

## The chat

Backend spawns headless Claude Code per message, from the project
root, output streamed to the browser:

- **Read-only by construction**: fixed args force plan/read behavior
  (no write tools approved); the cockpit does not offer a bypass.
  `claude_args` in config may *narrow* this further, never widen it.
- **Distilled reply, not raw stream-json**: every raw output line
  reaches the browser (an early per-line "line" frame, for the live
  streaming feel), but what the chat actually *renders* is the
  stream's terminal `{"type":"result", result: "..."}` event's
  `result` field — the same value MagnaFlow.WorkerController.Agents.
  ClaudeCodeRunner already harvests as `FinalText` — rendered through
  the same vendored markdown renderer the lane views use. The raw
  per-line JSON is plumbing, never shown as such.
- **Continuity**: the returned `session_id` (JSON output contract,
  same mechanism the worker uses) is kept per browser chat and
  resumed on the next message.
- **Escalation to action**: a "make this a command" button takes the
  chat's last answer (or user-edited text) into `POST .../commands` —
  a `draft` cmd file in the lane. The chat never executes work; the
  lane does. One place where work happens, and the cockpit stays a
  window you can also talk through.
- **Busy indicator**: if any cmd in the project is `running`, the
  chat page shows it — a chat question then shares the machine (and
  possibly rate limits) with a live run. Informational only, no lock.

## Where it runs

Anywhere with a working copy — the config points at paths, no
machine detection (same honesty as mf-watch's `git_sync`). Typical:
the worker machine, because its copy is freshest (runs commit there)
and it is always on; view remotely over Tailscale/LAN. Default bind
is `localhost`; binding `0.0.0.0` is an explicit config choice, and
v0.1 has **no auth** — exposing it beyond a trusted network/VPN is on
the operator. On a machine where the lane arrives by git, what the
cockpit shows is as fresh as the last pull — the watcher tail tells
you when that was.

## Look

Dark theme derived from `docs/images/magnaflow-banner-wide.png`
(header image; `magnaflow-logo.png` / `.ico` as logo/favicon — copy
into `wwwroot/assets/`). Measured palette, as CSS variables:

```css
--bg: #05060f;           /* night sky, page background */
--bg-raised: #0d0b18;    /* cards, header */
--panel: #0a1220;        /* deep-blue panel */
--border: #16233a;       /* horizon blue */
--text: #e6e4dd;         /* warm off-white */
--muted: #7f8ca3;        /* dimmed blue-grey */
--lava-deep: #802000;    /* deep ember */
--lava: #dc5707;         /* primary accent */
--lava-bright: #ff8c1a;  /* highlights, links, active */
```

Status colors stay in-theme: `draft` muted, `ready` lava-bright,
`running` lava (pulsing), `questions` amber `#ffc04d`, `done` calm
green `#6fbf73`, `aborted` red `#e5484d`. Lava is the accent, not
the wallpaper: large surfaces stay dark blue-black, orange marks
what is alive or needs eyes.

## Tech

C# / .NET 10, ASP.NET Core minimal API + static files (Kestrel),
`tools/mf-cockpit/` — same toolchain and conventions as the worker
controller and mf-watch. Frontend: hand-written HTML/CSS/JS, no
build step, no CDN dependencies (must work on a LAN without
internet); markdown rendered client-side by a small vendored
renderer. Config `mf-cockpit.yml` next to the binary or `--config`,
YAML, every field defaulted (`port: 5210`, `bind: localhost`,
`projects: []`, `chat.enabled: true`, `chat.command: claude`,
`chat.args: []`).

> **Updated:** mf-cockpit.yml was merged into one per-machine
> `magnaflow.yml`, under a `cockpit:` section, alongside mf-watch's
> `watch:` section — see `docs/fase7-machine-config/`. The old
> filename still works next to the binary as a deprecated fallback.

No library reference to the worker controller or mf-watch — the
cockpit knows frontmatter statuses, file layouts, and exit codes,
nothing of internal types. Same decoupling mf-watch proved.

## Guards

- Path safety: every file the API serves must resolve inside the
  project root (specs browse takes a user path — normalize and
  reject escapes).
- Log tails are bounded (default last 200 lines / 64 KB) — evidence
  logs can be huge.
- Lane writes validate the id is unique (next free NNNN) and the
  status transition is exactly draft→ready; anything else is a 409.
- The chat process gets a hard timeout (config, default 5 min) and
  is killed on client disconnect.

## Not in scope (v0.1)

Auth/HTTPS (VPN is the perimeter), historical metrics or charts,
further action buttons (git commit/push of hand-edits, new branch,
retry an aborted cmd, answer qa files in the browser — natural v0.x
candidates, each added as an explicit human-initiated action per the
invariant above; hand-edit + commit remains the path until then),
parallel-run awareness beyond the busy indicator, mobile layout,
tray/daemon packaging (run it under the OS's own service mechanism
if wanted; `--urls` style overrides suffice).
