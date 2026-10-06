# mf-watch — MagnaFlow prompt-lane dispatcher (v0.1)

A small daemon that polls a target project's `docs/prompts/` for
`NNNN-cmd-name.md` files with `status: ready` and hands them to `mf-worker`,
one at a time. Each cycle: pull (optional), scan, dispatch, push (optional),
notify, then sleep with adaptive backoff. A human can do every step by hand.
Behavior: [mf-watch spec](../../docs/specs/watch/mf-watch.md). Why it works
this way: [decision 0006](../../docs/decisions/0006-mf-watch-design.md).

It spawns `mf-worker` as a separate process (no library reference to
[`tools/worker-controller/`](../worker-controller/)) and reads only a
command's `status:` frontmatter and the worker's exit code.

Walkthrough, including a smoke test without an agent:
[EXAMPLES.md](EXAMPLES.md).

## Build

```powershell
cd tools\mf-watch
dotnet build
dotnet test
```

Needs the .NET 10 SDK, git, and the `mf-worker` binary (or `worker.command`
set to its real path) wherever `git_sync` or dispatch actually run.

## Run

```text
mf-watch [--project <path>] [--config <path>] [--once]
```

- `--project <path>`: project root containing `docs/prompts/` (default:
  current directory).
- `--config <path>`: `mf-watch.yml` path (default: `mf-watch.yml` next to the
  binary).
- `--once`: one poll cycle, then exit. For validation, or to use an OS
  scheduler (cron, Task Scheduler) instead of the loop.

Output always goes to the console and is appended to
`.magnaflow/mf-watch.log` in the target project.

`.magnaflow/mf-watch.lock` refuses a second instance on the same project,
including a `--once` racing a running daemon. The lock is exclusive on every
OS (Windows: share mode; Linux/macOS: `flock`). While held, the file holds the
PID and start time on two plain-text lines, the same shape as mf-run's `.pid`
file, so other tools (first: mf-cockpit's watch toggle) can check whether an
instance is really running. On Linux/macOS `cat` can read it, because `flock`
is advisory. Details: [Instance lock](../../docs/specs/watch/mf-watch.md#instance-lock).

## Waking it early (`.magnaflow/mf-watch.wake`)

After `idle_grace` with nothing to do, the interval backs off to
`interval_max` (15 min by default). To poll now after setting a command to
`ready`, create an empty wake file in the watched project:

```powershell
New-Item -ItemType File .magnaflow\mf-watch.wake   # touch .magnaflow/mf-watch.wake on Linux/macOS
```

The sleep runs in ~1 s slices, so the poll starts within about a second and
logs `wake requested (.magnaflow/mf-watch.wake) — polling now`. mf-watch
deletes the file, which makes the wake one-shot. A wake is not activity: it
resets neither the backoff interval nor the idle window (work found by that
poll resets them as usual). Only a running mf-watch consumes the file; with
nothing running it waits for the next instance. mf-cockpit's `Check now`
button (`POST /api/projects/{name}/watch/check-now`) writes exactly this file
and nothing else.

## Config (`mf-watch.yml`)

Every field is optional; a missing file or field uses the default shown:

```yaml
git_sync: false                # pull before scanning, push after dispatching (FR: see design doc)
worker:
  command: mf-worker            # executable to spawn per ready command; swap for a stub in tests
  args: []                      # extra argv appended after "run --project <root> <id>"
notify_command: null            # shell template, e.g.: notify-send "{title}" "{message}"
interval_min_minutes: 1
interval_max_minutes: 15
idle_grace_minutes: 30
worker_timeout_minutes: 120
```

`worker.command`/`worker.args` work like the worker's own
`agent.command`/`agent.args` (`MagnaFlow.WorkerController.Config.ProjectConfig`):
swap in a stub executable to test end to end without a real
`mf-worker` or agent.

## Notifications

`notify_command` fires (on top of the console/log line) when the human is
next at bat: a run finished (`done`/`aborted`), a run raised `questions`, or
an error occurred (git pull/push failure, worker exited unexpectedly, a
`running` command left stuck by an earlier crash). `{title}` and `{message}`
are substituted verbatim. Titles and timing:
[Notifications](../../docs/specs/watch/mf-watch.md#notifications).

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Ran to completion (one poll with `--once`, or a clean shutdown of the daemon loop) |
| 2 | Usage or configuration error (bad flag, invalid `mf-watch.yml`) |
| 3 | Another mf-watch instance already holds the lock for this project |
| 4 | `git_sync` is enabled but git is not available on this machine |
