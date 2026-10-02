# mf-cockpit v0.3 — design: Run & Config (2026-07-14)

The service card mf-run's design deferred, plus in-browser editing
of the project config. Goal in one sentence: from the cockpit you
can see every project's services, start and stop them, follow the
url to the running debug instance, and adjust
`.magnaflow/config.yml` — without an editor or SSH session on the
worker machine. Extends `docs/fase5-cockpit/ontwerp-v0.1.md`; its
invariant still governs, with one clarification below.

## Invariant clarification (v0.1 wording, re-scoped)

v0.1 says the cockpit "never touches `.magnaflow/`". What that
clause protects is the **run evidence** (`.magnaflow/<id>/` — the
worker's own diary) and it keeps meaning exactly that. Two things
in `.magnaflow/` are *not* evidence and are touched here, each
within the existing rules:

- `.magnaflow/config.yml` becomes editable — **write #5**: a
  human-initiated save, landing in the existing plain-text format,
  committed immediately (`cockpit: edit config`). Git is the undo.
- `.magnaflow/run/` (PID files, service logs) is read for display
  only, and is gitignored anyway — the run *actions* below write
  nothing in git; they only touch OS processes, and only through
  mf-run.

## The Run card (`project.html`)

Per configured service: name, a running/stopped dot, the `url` as a
clickable link (this is the "open the debug build from my own
machine" moment), an expandable tail of `.magnaflow/run/
<service>.log` (the existing bounded tail reader — start failures
land there), and start / stop / restart buttons. Card header:
start all / stop all. No services configured → no card.

The cockpit **never manages processes itself** — every status read
and every button is a shell-out to the mf-run binary, exactly as
mf-run's design intended ("the cockpit later gets a status dot and
start/stop buttons as plain shell-outs instead of duplicated
process logic"). `run.command` in the cockpit's machine config
(`magnaflow.yml`, `cockpit:` section — fase 7; default `mf-run`)
is the substitutable seam, same stub mechanism as `chat.command`.
This design assumes fase 7 (the machine-config merge) lands first.

`index.html`'s project rows gain a small `run: k/n` chip (services
running / configured), fetched asynchronously per row — a status
call spawns a process, so it stays off the fast summary path, the
same split the git card already uses.

## mf-run: `status --json`

The one change outside the cockpit this fase carries along: `mf-run
status` gains a `--json` flag printing an array of
`{name, running, pid?, url?}` — exit-code semantics unchanged.
Parsing the human-facing lines would be fragile and would turn
their wording into an accidental API; a flag is honest. Covered by
mf-run's own test suite.

## API

- `GET /api/projects/{name}/run` — shell-out `status --json`,
  passed through (plus a `configured: false` shape when the project
  has no `run:` block).
- `POST /api/projects/{name}/run/start|stop` — all services.
- `POST /api/projects/{name}/run/{service}/start|stop|restart` —
  one service; 404 when the name isn't in the project's config.
- Every action response carries mf-run's exit code and captured
  output, rendered inline on the card (a start failure's log tail
  is exactly what you want to read right there).
- SSE: the existing `.magnaflow/` watcher fans out changes under
  `.magnaflow/run/` as a new coarse kind `run`, so open pages
  refresh the card after an action or an mf-worker-triggered
  stop/start — same debounce, no payloads, as ever.

## The Config page (`config.html?p=<name>`)

One file, `.magnaflow/config.yml`, shown two ways:

- **A parsed summary card** (read-only): build/test commands,
  retry limit, the `run.services` list — the server already parses
  the file for validation, the card is free.
- **A raw YAML editor** (monospace textarea) with Save.

Raw text, deliberately not a form: a form that regenerates YAML
destroys comments and ordering, which means state would exist only
inside the cockpit — the exact thing the invariant forbids. The
file stays the source of truth; the cockpit stays a window you can
type in. A structured helper for common edits (e.g. add a service)
can sit on top later, generating a *text* edit.

Save flow (`PUT /api/projects/{name}/config`, body
`{content, baseHash}`):

1. `baseHash` (content hash handed out by the GET) must still
   match the file on disk — 409 otherwise: someone (or some run)
   changed it since you loaded the page; reload and redo.
2. The content must parse as YAML — 400 with the parse error
   positioned for display. Unknown top-level keys are a returned
   *warning*, never a rejection: the config file is shared by
   tools with different vocabularies, and the cockpit must not
   become the schema police for all of them.
3. Write + `git add` + `git commit -m "cockpit: edit config"` —
   immediately, like every other write (dirty-tree guard, git_sync
   outbox: same reasoning as v0.1).

If any command in the project is `running`, the page shows a
warning banner — the worker read the config at run start, so the
edit takes effect next run. Warn, don't block: editing during a
run is harmless, surprising silence would not be.

## Guards

- The editable path is exactly `.magnaflow/config.yml` under the
  project root — the endpoint takes no path parameter at all, so
  there is nothing to escape.
- Content size cap (256 KB) on PUT.
- Hash-based optimistic concurrency (above).
- mf-run spawns get a hard timeout (config, default 60 s — start's
  own liveness wait is ~2 s per service, so this only trips on
  something truly wedged) and the endpoint reports the kill.
- Bind/auth stance unchanged from v0.1 (VPN/LAN is the perimeter)
  — but note it now covers *starting and stopping processes*
  remotely, so binding beyond localhost is even more explicitly
  the operator's call.

## Not in scope (v0.3)

Editing the machine config (`magnaflow.yml` — it is per machine,
possibly a different machine than the browser's; the cockpit
editing its own config is self-surgery), a structured form editor,
service log live-streaming (the bounded tail + SSE refresh
suffices), health checks or URL probing (the dot means "process
alive", mf-run's own contract), auth, and write #4 ("commit all")
— still pending from the v0.1 design, unchanged by this fase.
