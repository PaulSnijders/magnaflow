# Technical

How a project's watcher is started, stopped, seen and nudged from
outside. mf-watch itself has no on/off concept per project. It runs one
project per process, selected by `--project` (see
[mf-watch](../watch/mf-watch.md)). Supervision is everything around that
process. It is coupled to mf-watch through two files only, never through
a library or a socket. Both files have a by-hand equivalent.

## The files

| File | Written by | Read by | Means |
|---|---|---|---|
| `.magnaflow/mf-watch.lock` | mf-watch, while running | cockpit | who holds the project: PID and start time |
| `.magnaflow/mf-watch.wake` | cockpit (Check now), `touch` | mf-watch | poll now, once |
| `.magnaflow/mf-watch.log` | mf-watch | cockpit | the diary, tailed on the Watcher card |

### Lock file {#lock-file}

Two lines: the PID, then the process start time (UTC, round-trip
ISO-8601). It stays readable while mf-watch holds it: on Windows for
any reader that shares write access, on Linux and macOS for plain
readers (`cat`). There the lock is an exclusive advisory `flock`, which
another .NET `FileStream` respects, so only Windows reads it from the
cockpit. Its *presence* is not liveness. A hard kill (Windows toggle Stop, `Stop-Process`,
possibly a systemd stop) leaves the file behind with stale content. The
rules:

- **Liveness** counts only when a process with that PID exists *and*
  its OS start time matches the recorded one within ±2 s. This is
  mf-run's PID-reuse guard, applied to this file. A mismatch is stale
  and is never killed.
- The cockpit's overview and the `lockPresent` field report plain
  presence. Only `running` (from the toggle, below) is the real
  liveness answer.
- A stale file needs no cleanup. The next mf-watch to start truncates
  and rewrites it.

### Wake file {#wake-file}

An empty file. A running, sleeping mf-watch deletes it and polls within
about 1 s. The cockpit writes it only if it is not there yet, because a
pending wake is already a request. Check now is disabled while no
watcher runs, since nothing would consume the file. The write is
identical on every OS, so it bypasses the platform split below.

## The cockpit toggle

`IWatchControl` has two implementations, chosen once at startup by OS.
The cockpit owns no PID record of its own, so a cockpit restart between
two clicks changes nothing.

| | Linux | Windows |
|---|---|---|
| Mechanism | systemd `--user` template unit `mf-watch@.service`, one instance per project | the cockpit spawns and kills `mf-watch --project <path>` itself |
| Unit / process name | `mf-watch@$(systemd-escape <path>).service`. `%I` passes the unescaped path to `--project` | `cockpit.watch.command` (default `mf-watch`, from `PATH`), detached, no window, no `--config` |
| Running? | `systemctl --user is-active` | lock file plus the PID/start-time guard |
| Start | `systemctl --user enable --now`. The answer is systemctl's exit code and output | spawn, unless already running. Success means the spawn did not throw. There is no post-start liveness check |
| Stop | `systemctl --user disable --now` | kill the PID from the lock file, with its whole process tree, only if the guard matches. Idempotent |
| Survives reboot / logout | yes, enabled. A headless machine needs `loginctl enable-linger` | **no**. Only the desktop shortcut's one hardcoded watcher comes back |
| Started by hand, outside the toggle | not shown as running (only the unit is asked) | shown as running (the lock file is the source) |

Consequences that must stay true:

- Stopping kills an in-flight mf-worker with the watcher: the process
  tree on Windows, the unit's control group on Linux. The command is
  then left `running` for a human to reset.
- On Windows a watcher that dies immediately (bad project path, invalid
  config) shows "started" and then simply never shows as running. Its
  stderr goes nowhere, because mf-watch logs only after it has started.
- The unit uses `Restart=no`, because mf-watch's exit codes carry
  meaning and are not a crash loop to paper over.
- Removing a project from the cockpit does not stop its watcher. A live
  unit or process survives the cockpit forgetting the project.

The toggle answers in the HTTP body (`{success, error}`), never with an
error status. An unknown project is 404. See the
[project page](../cockpit/project.md#watcher).

DRAFT: generated from code, not human-reviewed.

Code: tools/mf-cockpit/src/MagnaFlow.MfCockpit/Watch/, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Api/WatchEndpoints.cs, tools/mf-watch/src/MagnaFlow.MfWatch/Runtime/InstanceLock.cs, tools/mf-watch/src/MagnaFlow.MfWatch/Polling/WakeFile.cs, tools/install/install.sh
Why: decisions/0011-cockpit-watch-toggle.md
