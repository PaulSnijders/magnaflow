# mf-cockpit — MagnaFlow read-only dashboard + chat (v0.1)

A small ASP.NET Core app (Kestrel, static files, no SPA framework). It
renders the plain-text state of one or more MagnaFlow projects (the prompt
lane, per-command evidence, the mf-watch diary, the spec tree) and offers a
read-only AI chat that can turn an answer into a new `draft` command. It is
a **window** on the system, never a second actor in it. Design:
[`docs/decisions/0007-cockpit-design.md`](../../docs/decisions/0007-cockpit-design.md);
what shipped: `git show c0e1353:docs/fase5-cockpit/v0.1-completion-notes.md`.
Per-page behavior lives in the state specs under
[`docs/specs/cockpit/`](../../docs/specs/cockpit/).

No library reference to `tools/worker-controller/` or `tools/mf-watch/`:
process spawns and file reads only, the same decoupling those two tools
keep.

## Build

Requires the .NET 10 SDK and git.

```powershell
cd tools\mf-cockpit
dotnet build
dotnet test
```

Run the built exe directly, not `dotnet run` (see [Where it runs](#where-it-runs)):

```powershell
.\src\MagnaFlow.MfCockpit\bin\Debug\net10.0\MagnaFlow.MfCockpit.exe --config C:\path\to\mf-cockpit.yml
```

## Config (`mf-cockpit.yml`)

A default copy ships next to the binary
(`src/MagnaFlow.MfCockpit/mf-cockpit.yml`, copied into `bin/` on every
build). Edit it in place, or pass `--config <path>`. Every field is
optional:

```yaml
port: 5210
bind: localhost
projects:
  - name: my-project
    path: C:/GIT/my-project
chat:
  enabled: true
  command: claude          # swap for a stub executable in tests, same mechanism the worker's
  args: []                 # and mf-watch's own validation used
  timeout_minutes: 5
watch:
  command: mf-watch         # Windows only — the toggle's spawn target; PATH-resolved by default
```

## Watcher toggle

A start/stop switch per project (Watcher card on `project.html`). The
implementation is picked once at startup by OS (see
[`docs/decisions/0011-cockpit-watch-toggle.md`](../../docs/decisions/0011-cockpit-watch-toggle.md)):

- **Linux**: shells out to `systemd-escape` / `systemctl --user` against
  the `mf-watch@.service` template unit (`tools/install/install.sh`). Real
  per-project supervision; survives reboot.
- **Windows**: no such unit, so the cockpit spawns/kills `mf-watch`
  itself. Liveness comes from mf-watch's own `.magnaflow/mf-watch.lock`
  (PID + start time), not a second PID record, so it works however the
  instance was started. Does not survive reboot/logout.

## Pages

- `index.html`: status counts per project + a cross-project attention
  list (`questions`, `aborted`, or `running` with no recent evidence).
- `project.html?p=<name>`: lane table, "new draft command" form, watcher
  tail, git info.
- `command.html?p=<name>&id=<id>`: cmd/pln/qa/rst side by side + evidence
  log tails.
- `chat.html?p=<name>`: the read-only chat; "make this a command" turns
  the last reply into a draft.
- `specs.html?p=<name>`: read-only spec tree browser.

Live updates come over one SSE endpoint (`/api/events`): project + coarse
kind only, clients re-fetch.

## The only two writes

Both use the normal `docs/prompts/` lane format and are committed
immediately:

1. `POST /api/projects/{name}/commands`: a new `status: draft` cmd file
   (next free `NNNN`).
2. `POST /api/projects/{name}/commands/{id}/ready`: flips exactly
   `draft` → `ready`; any other current status is a 409.

## Where it runs

`ContentRootPath` is pinned to the executable's directory
(`AppContext.BaseDirectory`), not the shell's current directory. So
`mf-cockpit.yml` and `wwwroot/` resolve next to the binary wherever you
launch it from. Default bind is `localhost`; `0.0.0.0` is an explicit
config choice. v0.1 has no auth: exposing it beyond a trusted network/VPN
is on the operator.

## Guards

- The specs browser resolves every path inside the project root. Escapes
  get a 400 or a 404, never file content from outside the root.
- Log tails are bounded (last 200 lines / 64 KB); evidence logs can be
  huge.
- Lane writes: unique next-free id, and only the `draft`→`ready`
  transition (409 otherwise).
- The chat process is read-only by construction (`--permission-mode plan`,
  never overridable by config), has a hard timeout, and is killed on
  client disconnect.
