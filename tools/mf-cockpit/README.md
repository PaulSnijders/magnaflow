# mf-cockpit — MagnaFlow read-only dashboard + chat (v0.1)

A small ASP.NET Core app (Kestrel, static files, no SPA framework) that renders one or more
MagnaFlow projects' plain-text state — the prompt lane, per-command evidence, the mf-watch
diary, and the spec tree — and offers a read-only AI chat that can escalate an answer into a new
`draft` command. It is a **window** on the system, never a second actor in it; see
[`docs/fase5-cockpit/ontwerp-v0.1.md`](../../docs/fase5-cockpit/ontwerp-v0.1.md) for the full
design and [`v0.1-completion-notes.md`](../../docs/fase5-cockpit/v0.1-completion-notes.md) for
what shipped.

No library reference to `tools/worker-controller/` or `tools/mf-watch/` — process spawn and file
reads only, same decoupling both of those prove.

## Build

```powershell
cd tools\mf-cockpit
dotnet build
dotnet test
```

Requires the .NET 10 SDK and git. Run the built exe directly (not `dotnet run` — see "Where it
runs" below):

```powershell
.\src\MagnaFlow.MfCockpit\bin\Debug\net10.0\MagnaFlow.MfCockpit.exe --config C:\path\to\mf-cockpit.yml
```

## Config (`mf-cockpit.yml`)

A default copy ships next to the binary (`src/MagnaFlow.MfCockpit/mf-cockpit.yml`, copied into
`bin/` on every build) — edit it in place, or point `--config <path>` at your own. Every field is
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

A start/stop switch per project (`project.html`'s Watcher card). Two implementations, chosen once
at startup by OS (see `docs/fase5-cockpit/watch-toggle-notes.md`): Linux shells out to
`systemd-escape`/`systemctl --user` against the `mf-watch@.service` template unit
(`tools/install/install.sh`) — real per-project supervision, survives reboot. Windows has no such
unit, so the cockpit spawns/kills `mf-watch` itself, reading liveness from mf-watch's own
externally-readable `.magnaflow/mf-watch.lock` (PID + start time) rather than tracking a second PID
record — works regardless of how that instance was started, but does not survive a reboot/logout.

## Pages

- `index.html` — every configured project's status counts + a cross-project attention list
  (`questions`, `aborted`, or `running` with no recent evidence activity).
- `project.html?p=<name>` — the lane table, a "new draft command" form, watcher tail, git info.
- `command.html?p=<name>&id=<id>` — cmd/pln/qa/rst rendered side by side + evidence log tails.
- `chat.html?p=<name>` — the read-only chat; "make this a command" escalates the last reply into
  a draft.
- `specs.html?p=<name>` — read-only spec tree browse.

Live updates via one SSE endpoint (`/api/events`, project + coarse kind only — clients re-fetch).

## The only two writes

Both go through the normal `docs/prompts/` lane format and are committed immediately:

1. `POST /api/projects/{name}/commands` — a new `status: draft` cmd file (next free `NNNN`).
2. `POST /api/projects/{name}/commands/{id}/ready` — flips exactly `draft` → `ready`; any other
   current status is a 409.

## Where it runs

`ContentRootPath` is pinned to the executable's own directory (`AppContext.BaseDirectory`), not
the shell's current directory — so both `mf-cockpit.yml` and `wwwroot/` resolve next to the binary
regardless of where you launch it from. Default bind is `localhost`; `0.0.0.0` is an explicit
config choice, and v0.1 has no auth — exposing it beyond a trusted network/VPN is on the operator.

## Guards

- Every path the specs browser serves is resolved and validated inside the project root; escapes
  are rejected (400) or simply don't exist (404) — never file content from outside the root.
- Log tails are bounded (last 200 lines / 64 KB) — evidence logs can be huge.
- Lane writes: unique next-free id, and exactly the `draft`→`ready` transition (409 otherwise).
- The chat process is read-only by construction (`--permission-mode plan`, never overridable by
  config), has a hard timeout, and is killed on client disconnect.
