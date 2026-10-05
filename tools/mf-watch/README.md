# mf-watch — MagnaFlow prompt-lane dispatcher (v0.1)

A small daemon that polls a target project's `docs/prompts/` for `NNNN-cmd-name.md` files with
`status: ready` and feeds them to `mf-worker`, one at a time. It does five things, forever:
pull (optional), scan, dispatch, push (optional), notify — then sleeps with adaptive backoff.
Everything it automates is a thing a human can do by hand; see
[`docs/decisions/0006-mf-watch-design.md`](../../docs/decisions/0006-mf-watch-design.md) for the full
design.

mf-watch spawns `mf-worker` as a separate process — no library reference to
[`tools/worker-controller/`](../worker-controller/). It only ever reads a command's `status:`
frontmatter and the worker's exit code.

Hands-on walkthrough, including a no-agent-required smoke test: [EXAMPLES.md](EXAMPLES.md).

## Build

```powershell
cd tools\mf-watch
dotnet build
dotnet test
```

Requires the .NET 10 SDK, git, and the `mf-worker` binary (or config `worker.command` pointed at
its actual path) on machines where `git_sync` or dispatch actually run.

## Run

```text
mf-watch [--project <path>] [--config <path>] [--once]
```

- `--project <path>` — target project root containing `docs/prompts/` (default: current directory).
- `--config <path>` — `mf-watch.yml` path (default: `mf-watch.yml` next to the binary).
- `--once` — run a single poll cycle and exit; for validation, or for anyone preferring an OS
  scheduler (cron, Task Scheduler) over the daemon loop.

Console output always happens; every poll, run, notification, and error is also appended to
`.magnaflow/mf-watch.log` under the target project. A lockfile at `.magnaflow/mf-watch.lock`
refuses a second instance against the same project (including a `--once` racing a running daemon).
The lockfile is externally readable (PID + start time, two lines, plain text) while held — mf-run's
own `.pid` file shape — so another tool can tell whether an instance is genuinely running without
owning any process logic of its own; mf-cockpit's watch toggle is the first consumer.

## Waking it early (`.magnaflow/mf-watch.wake`)

After `idle_grace` with nothing to do, the interval backs off to `interval_max` (15 min by default)
— right for an idle project, but annoying when you have *just* set a command to `ready`. Creating
an empty file at `.magnaflow/mf-watch.wake` under the watched project asks the running daemon to
poll now instead of finishing its sleep:

```powershell
New-Item -ItemType File .magnaflow\mf-watch.wake   # touch .magnaflow/mf-watch.wake on Linux/macOS
```

The sleep is checked in ~1 s slices, so the next poll starts within about a second and logs
`wake requested (.magnaflow/mf-watch.wake) — polling now`. mf-watch **deletes** the file, which is
what makes the wake one-shot. A wake is not activity: it does not reset the backoff interval or the
idle window — if the poll it triggers actually finds work, the normal reset rule applies anyway.
Only a *running* mf-watch consumes the file; written with nothing running it simply waits for the
next instance. mf-cockpit's `Check now` button (`POST /api/projects/{name}/watch/check-now`) writes
exactly this file and nothing else.

## Config (`mf-watch.yml`)

Every field is optional — an absent file or absent field falls back to the default shown:

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

`worker.command`/`worker.args` are the same swap-the-executable mechanism
`MagnaFlow.WorkerController.Config.ProjectConfig`'s `agent.command`/`agent.args` use for the
worker's own AI agent — replacing it with a stub executable is how this project's own end-to-end
validation runs without a real `mf-worker`/agent in the loop.

## Notifications

`notify_command` fires (in addition to the console/log line, which always happens) when the
human is next at bat: a run finished (`done`/`aborted`), a run raised `questions`, or an error
occurred (git pull/push failure, worker exited unexpectedly, a `running` command found stuck from
a previous crash). `{title}` and `{message}` are substituted verbatim into the template.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Ran to completion (one poll with `--once`, or a clean shutdown of the daemon loop) |
| 2 | Usage or configuration error (bad flag, invalid `mf-watch.yml`) |
| 3 | Another mf-watch instance already holds the lock for this project |
| 4 | `git_sync` is enabled but git is not available on this machine |
