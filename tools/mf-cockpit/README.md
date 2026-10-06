# mf-cockpit — MagnaFlow dashboard + chat

A small ASP.NET Core app (Kestrel, static files, no SPA framework). It
renders the plain-text state of one or more MagnaFlow projects (the prompt
lane, per-command evidence, the mf-watch diary, the spec tree) and offers a
read-only AI chat that can turn an answer into a new `draft` command.

It is a **window** on the system, not an actor in it. It has buttons, but
every write is a human click, lands in the existing plain-text/git
formats, and could be done by hand. See [What it writes](#what-it-writes).
Design: [`docs/decisions/0007-cockpit-design.md`](../../docs/decisions/0007-cockpit-design.md)
and its successors (0008, 0009, 0011, 0013, 0014). Per-page behavior
lives in the state specs under
[`docs/specs/cockpit/`](../../docs/specs/cockpit/).

No library reference to `tools/worker-controller/`, `tools/mf-watch/` or
`tools/mf-run/`: process spawns and file reads only, the same decoupling
those tools keep.

## Build

Requires the .NET 10 SDK and git.

```powershell
cd tools\mf-cockpit
dotnet build
dotnet test
```

Run the built exe directly, not `dotnet run` (see [Where it runs](#where-it-runs)):

```powershell
.\src\MagnaFlow.MfCockpit\bin\Debug\net10.0\MagnaFlow.MfCockpit.exe --config C:\path\to\magnaflow.yml
```

## Config (`magnaflow.yml`, `cockpit:` section)

The cockpit reads the `cockpit:` section of the per-machine
`magnaflow.yml` and ignores every other section. Every field is
optional; no file at all means all defaults. A minimal example:

```yaml
cockpit:
  port: 5210
  bind: localhost
  projects:
    - name: my-project
      path: C:/GIT/my-project
```

The full key list (`chat:`, `run:`, `watch:`, `new_project:`), the lookup
order (`--config`, next to the binary, the user config dir) and the legacy
`mf-cockpit.yml` are in
[machine config](../../docs/specs/concepts/machine-config.md).

`mf-cockpit.yml` is the old flat file name. It still loads, with a
deprecation notice on stderr, but Add/Remove project refuse to edit it:
merge it into `magnaflow.yml` under `cockpit:` first. The build still
copies a default `mf-cockpit.yml` next to the binary, so a dev build
with no `magnaflow.yml` anywhere runs on that legacy file. The installers
delete it.

## Watcher toggle

A start/stop switch per project (Watcher card on `project.html`). The
implementation is picked once at startup by OS:

- **Linux**: `systemctl --user` against the `mf-watch@.service` template
  unit (`tools/install/install.sh`). Survives reboot.
- **Windows**: the cockpit spawns/kills `mf-watch` itself
  (`watch.command` under `cockpit:`). Liveness comes from mf-watch's own
  `.magnaflow/mf-watch.lock`. Does not survive reboot/logout.

Details: [watch supervision](../../docs/specs/concepts/watch-supervision.md);
why: [`docs/decisions/0011-cockpit-watch-toggle.md`](../../docs/decisions/0011-cockpit-watch-toggle.md).

## Pages

- `index.html`: all projects with status counts, a cross-project
  attention list, and Add project ([spec](../../docs/specs/cockpit/index.md)).
- `project.html?p=<name>`: Check now, Run card (mf-run services), lane,
  new draft, Git card, Watcher card
  ([spec](../../docs/specs/cockpit/project.md)).
- `command.html?p=<name>&id=<id>`: cmd/pln/qa/rst, evidence log tails,
  Make ready, follow-up draft ([spec](../../docs/specs/cockpit/command.md)).
- `chat.html?p=<name>`: the read-only chat; "make this a command" turns
  the last reply into a draft ([spec](../../docs/specs/cockpit/chat.md)).
- `specs.html?p=<name>`: read-only spec tree browser
  ([spec](../../docs/specs/cockpit/specs.md)).
- `config.html?p=<name>`: the cockpit's resolved setup and log, the
  `.magnaflow/config.yml` editor, Remove project
  ([spec](../../docs/specs/cockpit/config.md)).
- `scratchpad.html?p=<name>`: a plain-text notepad per project
  ([spec](../../docs/specs/cockpit/scratchpad.md)).

Live updates come over one SSE endpoint (`/api/events`): project + coarse
kind only, clients re-fetch.

## What it writes

Every write is a button click, guarded on the server (409/400 when the
rule the button shows fails). In short:

- **Lane** (committed at once): new draft, `draft` → `ready`, follow-up
  draft on a `done`/`aborted` command, a draft from a chat reply.
- **Project config**: the `.magnaflow/config.yml` editor (hash check
  against concurrent edits, YAML must parse; committed).
- **Git**: branch switch, `git pull --ff-only`, Sync (`git pull --rebase`
  + push, offered when the copy is both ahead and behind), commit all +
  push. Switch, pull and sync are refused on a dirty tree or while a
  command runs.
- **Processes**: start/stop/restart services via `mf-run`, start/stop
  the watcher, Check now (writes `.magnaflow/mf-watch.wake`).
- **Machine config**: Add project (existing or newly scaffolded) and
  Remove project, as narrow text edits of `magnaflow.yml` with a `.bak`
  and verify-or-restore. Remove never deletes files.
- **Scratchpad**: the one store outside git, kept next to
  `magnaflow.yml`, never in a working copy.

It never writes run evidence (`.magnaflow/<id>/`) and never edits specs.
The exact rules are in the page specs linked under [Pages](#pages).

## Where it runs

`ContentRootPath` is pinned to the executable's directory
(`AppContext.BaseDirectory`), not the shell's current directory. So
`wwwroot/` and a `magnaflow.yml` next to the binary resolve wherever you
launch it from. Default bind is `localhost`; `0.0.0.0` is an explicit
config choice. There is no auth: exposing it beyond a trusted
network/VPN is on the operator.

## Guards

- The specs browser resolves every path inside `docs/specs/`. Escapes
  get a 400 or a 404, never file content from outside the root.
- Log tails are bounded (last 200 lines / 64 KB); evidence logs can be
  huge.
- Lane writes: unique next-free id; Ready flips only `draft` → `ready`,
  and a follow-up only starts from `done`/`aborted` (409 otherwise).
- Config and scratchpad saves refuse a stale base hash (409) and content
  over 256 KB (400).
- The chat process is read-only by construction (`--permission-mode plan`,
  never overridable by config), has a hard timeout, and is killed on
  client disconnect.
