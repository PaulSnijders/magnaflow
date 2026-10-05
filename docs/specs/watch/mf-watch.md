# Technical

`mf-watch` is the dispatcher: a long-running loop over one project that
polls the lane, hands every `ready` command to mf-worker, and tells the
human when they are at bat. It reads frontmatter statuses and exit codes,
nothing else. The worker's own status transitions are the only truth
about what happened. Everything it automates (pull, run, push, notice) a
human can do by hand.

## Usage

```text
mf-watch [--project <path>] [--config <path>] [--once]
```

- `--project`: the project root holding `docs/prompts/`; default the
  current directory. One process watches exactly one project. Several
  projects means several processes, see
  [watch supervision](../concepts/watch-supervision.md).
- `--config`: the machine `magnaflow.yml`; default the lookup in
  [machine config](../concepts/machine-config.md#lookup-order). Only the
  `watch:` section is read.
- `--once`: one poll, then exit. For validation, or for an OS scheduler
  instead of the loop. A scheduler loses the adaptive backoff and the
  per-process stale-running memory (below).
- A flag that expects a value but is the last argument counts as unknown.

## Config (`watch:`)

```yaml
watch:
  git_sync: false            # pull before the scan, push after dispatch
  worker:
    command: mf-worker       # swap for a stub to test without an agent
    args: []                 # appended after `run --project <root> <id>`
  notify_command: null       # shell template with {title} and {message}
  interval_min_minutes: 1
  interval_max_minutes: 15
  idle_grace_minutes: 30
  worker_timeout_minutes: 120
```

Every field is optional. A missing file means all defaults. Validation
refuses only a non-positive `interval_min_minutes`, and an
`interval_max_minutes` below `interval_min_minutes` when both are set. A
max that is only lower than the *default* min is clamped up to min
silently. `git_sync` is an explicit setting, never detected: on the
machine where commands are authored the copy is already current. On a
remote worker machine, pull is the inbox and push the outbox.

## One poll

1. `git_sync`: `git pull --quiet`. "New commits" means `HEAD` moved.
2. Scan `docs/prompts/` for `NNNN[B-Z]?-cmd-<name>.md`, in ordinal
   filename order. The id is `NNNN[B-Z]?-<name>`, so a follow-up such as
   `0005B` is just another id. Only the frontmatter `status` and `title`
   are read. Status is trimmed and lowercased. A file with missing or
   invalid frontmatter is skipped with a `scan warning:` log line.
3. Every `running` command not seen before by this process is reported
   once as stale (logged and notified). mf-watch never picks it up
   again. Resetting it is the human's job, as with `questions`.
4. Each `ready` command, strictly one at a time: spawn
   `<worker.command> run --project <root> <id> <worker.args...>` in the
   project root. Each output line is logged prefixed `[<id>]`. Then
   re-read that one file's status. A worker exit of 0 or 1 that leaves
   `questions`, `done` or `aborted` is normal. Anything else is an error:
   another exit code, a timeout after `worker_timeout_minutes`, or any
   other status.
5. `git_sync`: `git push --quiet`, after every poll, even when nothing ran.

A failed pull or push is logged and notified but never stops the poll.
Whatever is on disk is still scanned and run.

Edge: a worker that refuses a command and leaves it `ready` (for example
a dirty tree, exit 3) gets the command dispatched again on every poll.
Each attempt counts as activity and raises an error notification.

## Adaptive backoff

`BackoffScheduler` is the whole scheduling policy. The interval stays at
`interval_min` while anything happens. Only after `idle_grace` with no
activity does it double per empty poll, up to `interval_max`. Activity
resets both the interval and the idle window at once. Activity means a
pull brought commits or a command was dispatched. Backing off is slow
and waking is instant on purpose: a slow wake-up costs a human waiting
time, a slow back-off costs nothing.

## Wake file

`.magnaflow/mf-watch.wake`: an empty file that cuts the current sleep
short. The sleep runs in ~1 s slices. When the file is found, mf-watch
deletes it (the delete makes it one-shot), logs
`wake requested (.magnaflow/mf-watch.wake) — polling now` and polls.

- A wake is not activity. It resets neither interval nor idle window.
- Only a sleeping loop consumes it. A file written while no watcher runs
  waits for the next instance. `--once` never consumes it.
- If the file is still held open by its writer, the next slice tries
  again.

Writers: `touch` by hand, or the cockpit's
[Check now](../cockpit/project.md#action-row).

## Instance lock

`.magnaflow/mf-watch.lock`: never two instances on one working copy. A
`--once` racing a running loop is refused too. The open write handle is
the lock, so the OS releases it however the process dies. While held,
the file is readable by others and contains two lines: the PID, then the
process start time (UTC, round-trip ISO-8601). This is the same shape as
mf-run's PID file, so readers can apply the same PID-reuse guard. On a
clean exit the file is deleted. A hard kill leaves it behind with stale
content. File presence alone therefore does not mean "running", see
[watch supervision](../concepts/watch-supervision.md#lock-file).

BUG: suspected, unverified. On Linux, .NET maps a write handle with
`FileShare.Read` to a shared `flock`, so a second instance may acquire
the lock too. The lock tests only run against the Windows semantics.

## Notifications

`notify_command` is the optional extra channel. Console and log output
always happen. The template is run through the shell (`cmd.exe /d /s /c`
or `/bin/sh -c`) with a 30 s timeout. `{title}` and `{message}` are
substituted verbatim, without quoting. A failing notifier is logged and
otherwise ignored. Pick a non-blocking notifier, because a modal one
stalls the loop.

| Title | When |
|---|---|
| `<id>: done` / `<id>: aborted` | run finished |
| `<id>: questions` | the human must answer |
| `<id>: error` | unexpected exit, timeout or status |
| `<id>: stale running` | found `running` without having started it |
| `mf-watch: git pull failed` / `git push failed` | sync failure |

The message carries the command's `title:` from its frontmatter. A title
with shell metacharacters reaches the shell as-is.

## Log

`.magnaflow/mf-watch.log` in the watched project is the dispatcher's
diary. It is append-only and never rotated, one line per event in the
form `yyyy-MM-ddTHH:mm:ss <message>` (local time). Every line also goes
to the console. It holds startup (project, config path, `once`), every
pull, spawn, worker output line, status outcome, notifier failure,
`sleeping <interval>`, wake and stop. The cockpit tails this file. It is
machine-local and never committed.

## Shutdown

Ctrl+C logs `shutdown requested, finishing current poll...` and exits 0.

BUG: the cancellation also reaches the in-flight mf-worker process, so
Ctrl+C kills a running worker instead of letting the poll finish. That
leaves the command `running`. The log line and the README promise
otherwise.

## Exit codes

A subset of the shared table in
[design](../concepts/design.md#shared-conventions). There is no 1: the
outcome of a dispatched command never shows in mf-watch's exit code.

| Code | Meaning |
|---|---|
| 0 | `--once` poll done, or clean shutdown of the loop |
| 2 | usage error, or invalid config (bad YAML, interval rules) |
| 3 | another instance holds the lock for this project |
| 4 | `git_sync` is on but git is not available |

The config is checked before the lock, and the lock before the git check.
A config deprecation notice
([legacy file names](../concepts/machine-config.md#legacy-file-names))
goes to stderr and the log but is never an error.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0006-mf-watch-design.md
