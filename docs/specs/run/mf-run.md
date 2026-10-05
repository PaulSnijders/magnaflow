# Technical

`mf-run` starts and stops a project's own long-running dev/debug
processes, so a worker run does not trip over locked ports and files,
and the fresh build is already up afterwards. One-shot CLI; it spawns
and kills OS processes and holds no state beyond PID files.

## Usage

```text
mf-run <start|stop|restart|status> [service] [--project <path>] [--json]
```

- `service`: act on one service; default all, in config order
  (`stop` and `restart` act in reverse order).
- `--project`: project root holding `.magnaflow/config.yml`; default the
  current directory.
- `--json`: `status` only; prints
  `[{name, running, pid?, url?, reason?, portListening?}]`. `reason` and
  `portListening` are additive. Consumers reading only
  `name/running/pid/url` must keep working.
- No `run:` block: every verb prints `no services configured`, exit 0.
  A console app with nothing to manage is the normal case.

## Config

Reads only the `run:` section of `.magnaflow/config.yml`. The rest of the
file belongs to the worker. See [machine and project config](../concepts/machine-config.md).

```yaml
run:
  services:
    - name: web                       # unique; duplicates fail validation
      command: src/App/bin/Debug/net10.0/App.exe   # the built exe, never `dotnet run`
      args: ["--urls", "http://0.0.0.0:5000"]      # optional, passed as argv
      workdir: src/App                # optional, default project root
      url: http://worker-1:5000       # optional; used only for the port check
```

`command` and `workdir` both resolve against the project root, not
against each other.

**Extensionless commands** resolve per OS so one committed config serves
every machine. The bare literal is tried first, then `.cmd`, `.bat`, `.exe`
(Windows) or `.sh` (Unix). The chosen path is logged on `start`. On a
miss, every tried path is listed. A `.sh` wrapper needs its executable
bit committed: it is exec'd directly, not through `sh`.

## Behavior that must stay stable

- **start**: detached spawn, stdout and stderr merged into
  `.magnaflow/run/<service>.log` (truncated per start), PID file
  `.magnaflow/run/<service>.pid` with the PID and the process start time
  in plain text. Waits about 2 s and fails with a log tail if the
  process died. Already running: no-op, exit 0.
- **stop**: kills the whole process tree. The orphaned child is the
  reason ports stay locked, so a partial kill defeats the point.
  Idempotent: if nothing is running or the PID file is stale, it is
  cleaned up silently and exits 0.
- **PID-reuse guard**: a PID counts as "ours" only when its OS start time
  matches the recorded one within ±2 s. A mismatch is stale: it is never
  killed and never reported as running.
- **Stale is never silent**: every stale verdict names its reason:
  `no-pid-file`, `process-gone`, or
  `starttime-mismatch: recorded <ts>, observed <ts>`.
- **status** reports two independent facts and never merges them:
  *tracked* (`running`, with `reason` when false) and *port listening* (a
  plain TCP connect to the `url` port; absent without `url`). A service
  started by hand shows `running: false, no-pid-file` and
  `portListening: true`. The cockpit's Run card combines the two
  ([project page](../cockpit/project.md#run)).

Everything mf-run does, a human can do by hand: start the exe, kill the
tree by PID, delete the PID file.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | all requested services running (`status`) or acted on |
| 1 | a requested service failed to start or stop, or is not running |
| 2 | usage or config error: unknown verb, unknown service, invalid `config.yml` |

## Integration

mf-worker spawns `mf-run stop` before a run and `mf-run start` after it,
as separate processes, and reads only the exit code. There is no library
reference in either direction.

Not in scope: health checks, log rotation, auto-restart, per-service
environment variables, auth or tunnels.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0010-mf-run-design.md
