# mf-run — MagnaFlow process manager (v0.1)

Starts and stops a project's own running app(s): the dev/debug instance on
the worker machine. Two reasons: Claude Code trips over locked ports and
files while the app runs, and after a run you want the fresh build already
up, so you can click through it remotely. The behavior contract is the
state spec [`docs/specs/run/mf-run.md`](../../docs/specs/run/mf-run.md);
the design is in
[`docs/decisions/0010-mf-run-design.md`](../../docs/decisions/0010-mf-run-design.md).

mf-run only spawns and kills OS processes. There is no library reference to
[`tools/worker-controller/`](../worker-controller/) or
[`tools/mf-watch/`](../mf-watch/) in either direction: `mf-worker` spawns
`mf-run stop`/`start` as separate processes and reads the exit code, just as
it spawns the AI agent.

## Build

```powershell
cd tools\mf-run
dotnet build
dotnet test
```

## Run

```text
mf-run <start|stop|restart|status> [service] [--project <path>] [--json]
```

- `service`: act on this service only. Default: all, in config order
  (`stop`/`restart` go in reverse order).
- `--project <path>`: project root holding `.magnaflow/config.yml`.
  Default: the current directory.
- `--json`: for `status`; see [Status](#status-semantics).

No `run:` block means nothing to do: every command prints
`no services configured` and exits 0. A console app is the normal case,
not an error.

## Config (`.magnaflow/config.yml`, `run:` section)

The same file the worker controller reads. mf-run ignores everything but
`run:`.

```yaml
run:
  services:
    - name: web
      command: src/MyApp/bin/Debug/net10.0/MyApp.exe   # the built executable, not `dotnet run`
      args: ["--urls", "http://0.0.0.0:5000"]           # optional
      workdir: src/MyApp                                 # optional, default: project root
      url: http://worker-1:5000                          # optional, informational only
```

`command` and `workdir` both resolve against the project root, not against
each other. Service names must be unique; validation fails fast.

### Cross-platform commands (per-OS wrapper resolution)

A `command` *with* an extension (`.exe`, `.cmd`, ...) is used as-is: one
exists-check on that path.

A `command` with **no extension** tries the bare literal first, then
per-OS wrapper suffixes in order:

- **Windows**: `<command>.cmd`, `<command>.bat`, `<command>.exe`
- **Unix**: `<command>.sh`

Why: one `config.yml`, checked into git and shared by every OS, can name a
wrapper without an OS-specific extension. Commit one wrapper per OS side
by side:

```text
web/mf-start-web.cmd
web/mf-start-web.sh
```

and reference it without extension:

```yaml
run:
  services:
    - name: web
      command: web/mf-start-web
```

Windows picks the `.cmd`, Linux/macOS pick the `.sh`. `start` logs the
pick (`<service>: resolved command to <path>`). If nothing matches, the
"not found" message lists every path tried.

**`.sh` wrappers need the executable bit.** On Unix mf-run runs
`exec "<path>" ...` directly, without `sh` in front (an `execve()` on the
file). So set the bit and commit it, or git drops it:
`chmod +x mf-start-web.sh` and
`git update-index --chmod=+x mf-start-web.sh`. Without the bit, the
wrapper resolves (the file exists) but fails to spawn.

**Args pass-through.** `args` are appended to the command line as
individually quoted arguments; the wrapper receives plain argv and never
re-parses a shell string. A `.sh` wrapper forwards them with `"$@"`, a
`.cmd` wrapper with `%*`. Keep the two wrappers mirrored.

## Process management

- **start**: spawns the service detached. stdout+stderr go to
  `.magnaflow/run/<service>.log` (truncated per start). Writes
  `.magnaflow/run/<service>.pid`: PID + process start time, plain text
  (`cat` suffices). Waits ~2s; if the process died, fails with a log tail.
  Already running: no-op, exit 0.
- **stop**: kills the whole process tree from the PID file. Locked ports
  come from an orphaned child, so a partial kill defeats the point.
  Idempotent: nothing running or a stale PID file is cleaned up silently,
  exit 0.
- **restart**: stop + start.
- **PID-reuse guard**: a PID is trusted only if its OS start time matches
  the recorded one within ±2s. Otherwise it is stale: never killed, never
  reported as running. The tolerance absorbs timestamp round-tripping and
  clock ticks; real PID reuse happens minutes to days later, so nothing is
  lost.
- **Stale is never silent**: each stale verdict logs its reason:
  `no-pid-file`, `process-gone`, or
  `starttime-mismatch: recorded <ts>, observed <ts>` (PID reused).

A human can do all of this by hand: start the exe, kill the tree by PID,
delete a PID file.

## Status semantics

`status` reports two independent facts per service and never merges them:

- **Tracked** (`running`): does mf-run's PID file point at a live,
  matching process? When false, `reason` is one of the three stale reasons
  above.
- **Port listening** (`port_listening`): does *anything* accept a plain
  TCP connect (no HTTP) on the port of the service's `url`? `true`/`false`,
  or absent without a `url`. A service started outside mf-run (e.g.
  `ng serve` by hand) shows `running: false`, reason `no-pid-file`, and
  `port_listening: true`.

mf-cockpit's Run card combines the two: tracked+listening → running; only
listening → "running (not started by mf-run)"; only tracked → "process up,
port not answering"; neither → stopped (with the reason).

`--json` prints `[{name, running, pid?, url?, reason?, portListening?}]`.
`reason` and `portListening` are additive, so consumers reading only
`name`/`running`/`pid`/`url` are unaffected.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success — all requested services running (`status`) or acted on successfully |
| 1 | At least one requested service failed to start, failed to stop, or isn't running |
| 2 | Usage or configuration error (unknown command, unknown service, invalid `config.yml`) |

## Not in scope (v0.1)

Health checks / URL polling, log rotation, auto-restart on file change,
per-service environment variables, auth or tunnels for remote access (the
network is the perimeter). A cockpit service card (status dot, start/stop
buttons) is designed against this CLI's exit-code contract but built
separately, later.
