# mf-run — MagnaFlow process manager (v0.1)

A small process manager for a project's own running application(s) — the dev/debug instance on
the worker machine. It exists because Claude Code trips over locked ports and files while the app
is running, and because after a run you want the fresh build already up so you can click through
it immediately, remotely. See
[`docs/decisions/0010-mf-run-design.md`](../../docs/decisions/0010-mf-run-design.md) for the full design.

mf-run spawns and kills OS processes — no library reference to
[`tools/worker-controller/`](../worker-controller/) or [`tools/mf-watch/`](../mf-watch/) in
either direction. `mf-worker` spawns `mf-run stop`/`start` as separate processes and reads the
exit code, same as it spawns the AI agent.

## Build

```powershell
cd tools\mf-run
dotnet build
dotnet test
```

## Run

```text
mf-run <start|stop|restart|status> [service] [--project <path>]
```

- `service` — only act on this one service (default: all, in config list order; `stop`/`restart`
  act in reverse order).
- `--project <path>` — target project root containing `.magnaflow/config.yml` (default: current
  directory).

A project with no `run:` block has nothing to do: every command prints `no services configured`
and exits 0 — a console app is the normal case, not an error.

## Config (`.magnaflow/config.yml`, `run:` section)

The same file the worker controller reads; mf-run only looks at `run:`, everything else is
ignored.

```yaml
run:
  services:
    - name: web
      command: src/MyApp/bin/Debug/net10.0/MyApp.exe   # the built executable, not `dotnet run`
      args: ["--urls", "http://0.0.0.0:5000"]           # optional
      workdir: src/MyApp                                 # optional, default: project root
      url: http://worker-1:5000                          # optional, informational only
```

Both `command` and `workdir` are resolved relative to the project root (not to each other). No
two services may share a name — config validation fails fast.

### Cross-platform commands (per-OS wrapper resolution)

A `command` *with* an extension (`.exe`, `.cmd`, ...) is resolved exactly as shown above: an
exists-check on that literal path, nothing more.

A `command` with **no extension** additionally tries per-OS wrapper suffixes, in order, before
reporting "not found": the bare literal is always tried first, then

- **Windows**: `<command>.cmd`, `<command>.bat`, `<command>.exe`
- **Unix**: `<command>.sh`

This exists so one `config.yml` (checked into git, shared by every OS the project runs on) can
reference a wrapper by name without baking in an OS-specific extension. The convention: commit
one wrapper per OS side by side —

```text
web/mf-start-web.cmd
web/mf-start-web.sh
```

— and reference them extensionless from config:

```yaml
run:
  services:
    - name: web
      command: web/mf-start-web
```

Windows picks up `mf-start-web.cmd`, Linux/macOS pick up `mf-start-web.sh`, same config either
way. Whichever file resolution picks is logged in `start` output
(`<service>: resolved command to <path>`), and if nothing matches, the "not found" message lists
every path that was tried.

**`.sh` wrappers need the executable bit.** mf-run's Unix spawn path runs `exec "<path>" ...`
directly (no explicit `sh` in front) — that's an `execve()` on the file itself, so it needs its
executable permission bit set (`chmod +x mf-start-web.sh`, and committed with that mode so git
preserves it: `git update-index --chmod=+x mf-start-web.sh`). A `.sh` wrapper without the
executable bit resolves fine (the file exists) but fails to spawn.

**Args pass-through.** `service.args` from config are appended to the spawned command line as
individually-quoted arguments — the wrapper itself never re-parses a shell string, it just
receives argv. A `.sh` wrapper forwards them to whatever it execs with `"$@"`, the same role
`%*` plays in a `.cmd` wrapper (`mf-start-web.sh` and `mf-start-web.cmd` should mirror each
other: same args-forwarding pattern, one per platform's shell).

## Process management

- **Start**: spawns the service detached (stdout+stderr merged into
  `.magnaflow/run/<service>.log`, truncated per start), writes `.magnaflow/run/<service>.pid`
  (PID + process start time, plain text — `cat` suffices), waits ~2s and reports failure with a
  log tail if the process died immediately. No-op with exit 0 if already running.
- **Stop**: kills the whole process tree recorded in the PID file — the reason ports stay locked
  is an orphaned child, so a partial kill would defeat the point. Idempotent: nothing running (or
  a stale PID file) is cleaned up silently, exit 0.
- **PID-reuse guard**: the recorded process start time is compared against the OS's own start
  time for that PID before anything is trusted as "the same process" — a recycled PID is treated
  as stale, never killed, never reported as running. The comparison tolerates up to ±2s of drift
  (timestamp round-tripping through the PID file's text and OS clock reporting can be off by a
  tick or two; real PID reuse happens minutes-to-days later, never sub-second, so the guard loses
  nothing by being tolerant at that scale).
- **restart** = stop + start.
- **Stale PID files are never silent**: whenever `start` or `stop` treats a PID file as stale
  (rather than pointing at the actually-running process it started), the log line names why:
  `no-pid-file` (nothing recorded), `process-gone` (recorded PID no longer exists), or
  `starttime-mismatch: recorded <ts>, observed <ts>` (the PID exists but is a different process —
  reuse).

Everything above is something a human can do by hand: start the exe, kill the tree by PID, delete
a PID file.

## Status semantics

`status` reports two independent facts per service, never merged into one guess:

- **Tracked**: does mf-run's own PID file still point at a live, matching process? This is the
  `running` field, exactly as before. When false, `reason` explains why — one of `no-pid-file`,
  `process-gone`, or `starttime-mismatch: recorded <ts>, observed <ts>` (see PID-reuse guard
  above).
- **Port listening**: is *something* answering on the service's configured `url` port right now?
  A plain TCP connect attempt (no HTTP), reported as `port_listening` — `true`/`false`, or absent
  when the service has no `url` to derive a port from. This is independent of PID tracking: a
  service started outside mf-run (e.g. `ng serve` by hand) shows `running: false` with a
  `no-pid-file` reason, yet `port_listening: true`.

mf-cockpit's Run card combines the two: tracked+listening → running; not tracked+listening →
"running (not started by mf-run)"; tracked+not listening → "process up, port not answering";
neither → stopped (with the reason shown).

`--json` output is `[{name, running, pid?, url?, reason?, portListening?}]` — `reason` and
`portListening` are additive; existing consumers reading only `name`/`running`/`pid`/`url` are
unaffected.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success — all requested services running (`status`) or acted on successfully |
| 1 | At least one requested service failed to start, failed to stop, or isn't running |
| 2 | Usage or configuration error (unknown command, unknown service, invalid `config.yml`) |

## Not in scope (v0.1)

Health checks / URL polling, log rotation, auto-restart on file change, per-service environment
variables, auth or tunnels for remote access (the network is the perimeter). A cockpit service
card (status dot, start/stop buttons) is designed against this CLI's exit-code contract but built
separately, later.
