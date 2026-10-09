# Technical

`mf-run` starts and stops a project's own long-running dev/debug
processes, so a worker run does not trip over locked ports and files,
and the fresh build is already up afterwards. One-shot CLI; it spawns
and kills OS processes and holds no state beyond PID files.

## Usage

```text
mf-run <start|stop|restart|status> [service] [--project <path>] [--json]
mf-run promote [--project <path>]
```

- `service`: act on one service; default all, in config order
  (`stop` and `restart` act in reverse order). The name `stable` acts
  on the [stable instance](#stable-instance). "All" never includes it.
- `--project`: project root holding `.magnaflow/config.yml`; default the
  current directory.
- `--json`: `status` only; prints
  `[{name, running, pid?, url?, reason?, portListening?}]`. `reason` and
  `portListening` are additive. Consumers reading only
  `name/running/pid/url` must keep working.
- `status` lists the services, then `stable` when configured.
- No `run:` block: every verb prints `no services configured`, exit 0.
  A console app with nothing to manage is the normal case. `promote`
  without `run.stable` prints `no stable instance configured`, exit 0.

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
against each other. A service named `stable` fails validation.

Optional, opt-in ([stable instance](#stable-instance)):

```yaml
run:
  stable:
    publish: Forecast/mf-publish-stable   # one argument: the absolute output dir
    command: Forecast.exe                 # resolved inside the published dir
    args: ["--urls", "http://localhost:7300"]
    url: http://localhost:7300            # port check, as for services
    link: https://host.tailnet.ts.net/app/   # optional; shown verbatim
    timeout_minutes: 15                   # publish limit, default 15
```

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

## Stable instance {#stable-instance}

A published build of the project that a worker run never stops, so the
human can keep using the app while the next command is being built. It is
one process on its own port, because the projects compile their frontend
into the backend. Everything lives machine-local under
`.magnaflow/stable/`:

| Path | Holds |
|---|---|
| `next/` | the publish target, emptied before every publish |
| `current/` | what runs; workdir and content root of the process |
| `prev/` | the previous `current/`, kept for a manual rollback |
| `state.yml` | `state`, `sha`, `dirty`, `at`, `message` |
| `promote.lock` | held while a promote runs |

The process uses the service files `.magnaflow/run/stable.pid` and
`stable.log`, and the same start, stop, PID-reuse and status rules as a
service. `publish` resolves like a service `command` (extensionless per
OS). `command` resolves the same way but inside `current/`, and the
published dir is the workdir.

**promote**, in this order:

1. Take `promote.lock`. If another promote holds it, exit 3 and change
   nothing.
2. Record `git rev-parse HEAD` and whether `git status --porcelain` is
   non-empty. Write `state: building` with that `sha`.
3. Empty `next/`, then run `publish <abs next/>` in the project root,
   with output to `.magnaflow/run/stable-publish.log` (truncated) and a
   limit of `timeout_minutes`. A non-zero exit, a timeout or a `next/`
   without `command` means `state: failed` with the reason, exit 1. The
   running instance is untouched.
4. Swap: stop the process, delete `prev/`, rename `current/` to `prev/`
   and `next/` to `current/`, then start. A rename is retried for a few
   seconds, because Windows keeps handles open briefly after a kill.
5. The new process must survive the usual ~2 s start check. If it does
   not, or a rename fails, put `prev/` back as `current/`, start it, and
   write `state: failed` ("new build did not start; previous restored"),
   exit 1.
6. Write `state: ready` with the recorded `sha`, exit 0.

`promote.lock` is an OS-level exclusive lock (as mf-watch's instance
lock), not a marker file, so a killed promote frees it. `state: building`
in `state.yml` with the lock free means the promote was interrupted, and
`status` reports it as `failed` ("promote interrupted").

**status** for `stable` adds, additively, `stable: true`, `state`, `sha`,
`dirty`, `at`, `message` and `link` to the JSON entry.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | all requested services running (`status`) or acted on |
| 1 | a requested service failed to start or stop, or is not running |
| 2 | usage or config error: unknown verb, unknown service, invalid `config.yml` |
| 3 | `promote` refused: another promote holds the lock |

## Integration

mf-worker spawns `mf-run stop` before a run and `mf-run start` after it,
and `mf-run promote` after a branchless `done`, as separate processes,
reading only the exit code
([worker run](../concepts/worker-run.md#mf-run-around-the-run)). The
cockpit spawns `promote` from its Run card. There is no library reference
in either direction.

Not in scope: health checks, log rotation, auto-restart, per-service
environment variables, auth or tunnels. The stable instance is not a
deploy: no remote target, no environments, one machine.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0010-mf-run-design.md, decisions/0018-stable-instance.md
