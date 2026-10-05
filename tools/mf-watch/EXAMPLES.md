# mf-watch — Examples

A hands-on tour of mf-watch, reusing the same `hello-website` example project the Worker
Controller ships with. Reference docs: [README.md](README.md) and the design it implements,
[`docs/decisions/0006-mf-watch-design.md`](../../docs/decisions/0006-mf-watch-design.md).

Commands below are PowerShell on Windows; the tool itself is cross-platform.

Two paths through this doc:

- **§2 — fast smoke test.** No AI agent, no API calls, done in under a minute. Validates
  mf-watch's own plumbing (polling, dispatch, status re-read, notify, lockfile) with a tiny stub
  standing in for `mf-worker`.
- **§3 — the real thing.** mf-watch dispatching the actual `mf-worker` binary, which in turn
  drives a real Claude Code agent.

## 0. Prerequisites

```powershell
dotnet --version    # .NET 10 SDK
git --version

# Build both tools once:
cd C:\GIT\magnaflow\tools\mf-watch
dotnet build
cd C:\GIT\magnaflow\tools\worker-controller
dotnet build

# Optional: aliases for convenience in this shell session
Set-Alias mf-watch  "C:\GIT\magnaflow\tools\mf-watch\src\MagnaFlow.MfWatch\bin\Debug\net10.0\MagnaFlow.MfWatch.exe"
Set-Alias mf-worker "C:\GIT\magnaflow\tools\worker-controller\src\MagnaFlow.WorkerController\bin\Debug\net10.0\MagnaFlow.WorkerController.exe"
```

## 1. What mf-watch needs

mf-watch points `--project` at any git repository laid out the way `mf-worker` expects (see the
Worker Controller's own [EXAMPLES.md, §1](../worker-controller/EXAMPLES.md), "What a target
project must contain"); it never needs to know more than a command's `status:` and its own exit
code.

Its own config, `mf-watch.yml`, is separate from that project's `.magnaflow/config.yml` — it
lives next to the `mf-watch` binary by default, or wherever `--config` points, so one mf-watch
install can watch any project:

```yaml
git_sync: false                 # pull before scanning, push after dispatching
worker:
  command: mf-worker             # swap for a stub to test without a real worker/agent — see §2
  args: []                       # extra argv appended after "run --project <root> <id>"
notify_command: null             # shell template, e.g.: notify-send "{title}" "{message}"
interval_min_minutes: 1
interval_max_minutes: 15
idle_grace_minutes: 30
worker_timeout_minutes: 120
```

Every field is optional; a missing `mf-watch.yml` is just every default at once.

## 2. Fast smoke test — no agent required

`worker.command` is swappable (the same substitution mechanism `mf-worker`'s own `agent.command`
uses): point it at anything that accepts `run --project <root> <id>` and mf-watch can't tell the
difference. Here's a stand-in for `mf-worker` that marks the command done and makes a real
bookkeeping commit (so §6's `git_sync` push has something genuine to push) — `.cmd` files run
directly on Windows without a shell wrapper, so this is the whole "binary":

```powershell
mkdir C:\tmp\mfwatch-smoketest
@'
@echo off
setlocal
set ID=%4
set NUM=%ID:~0,4%
set NAME=%ID:~5%
set REL=docs\prompts\%NUM%-cmd-%NAME%.md
set FILE=%3\%REL%
powershell -NoProfile -Command "(Get-Content -Raw '%FILE%') -replace '(?m)^status:\s*ready\s*$', 'status: done' | Set-Content -NoNewline '%FILE%'"
git -C %3 add -- %REL%
git -C %3 commit --quiet -m "stub-worker: %ID% -> done"
echo stub-worker: %ID% -^> done
exit /b 0
'@ | Set-Content C:\tmp\mfwatch-smoketest\stub-worker.cmd
```

Set up a throwaway project with one ready command:

```powershell
mkdir C:\tmp\mfwatch-smoketest\proj\docs\prompts -Force
cd C:\tmp\mfwatch-smoketest\proj
git init -b main
git config user.email you@example.com
git config user.name  you

@'
---
title: Say hello
status: ready
attempts: 0
---

## Goal
Do something useful.
'@ | Set-Content docs\prompts\0001-cmd-hello.md

git add -A
git commit -m "seed: 0001-hello ready"

@'
worker:
  command: C:\tmp\mfwatch-smoketest\stub-worker.cmd
'@ | Set-Content mf-watch.yml
```

Run one poll cycle:

```powershell
mf-watch --project C:\tmp\mfwatch-smoketest\proj --config C:\tmp\mfwatch-smoketest\proj\mf-watch.yml --once
```

```text
2026-07-10T11:35:07 mf-watch starting: project=C:\tmp\mfwatch-smoketest\proj config=...\mf-watch.yml once=True
2026-07-10T11:35:07 [0001-hello] spawning worker
2026-07-10T11:35:07 [0001-hello] stub-worker: 0001-hello -> done
2026-07-10T11:35:07 [0001-hello] worker finished (exit 0); status is now 'done'
2026-07-10T11:35:07 mf-watch stopped
```

```powershell
cat docs\prompts\0001-cmd-hello.md   # status: done
cat .magnaflow\mf-watch.log          # the same lines, persisted
```

Run it again — nothing is `ready`, so it's a true no-op (exit 0, no process spawned, no log
noise beyond the start/stop lines):

```powershell
mf-watch --project C:\tmp\mfwatch-smoketest\proj --config C:\tmp\mfwatch-smoketest\proj\mf-watch.yml --once
```

## 3. The real thing — dispatching mf-worker

Copy the example project (needs its own git history, same as when running `mf-worker` directly):

```powershell
Copy-Item -Recurse C:\GIT\magnaflow\tools\worker-controller\examples\hello-website C:\tmp\hello-website
cd C:\tmp\hello-website
git init -b main
git add -A
git commit -m "hello-website: project skeleton + commands"
```

Point mf-watch at the real `mf-worker` binary (the `mf-worker` alias from §0 won't resolve
inside a spawned child process unless it's actually on `PATH`, so use the full path):

```powershell
@'
worker:
  command: C:\GIT\magnaflow\tools\worker-controller\src\MagnaFlow.WorkerController\bin\Debug\net10.0\MagnaFlow.WorkerController.exe
'@ | Set-Content C:\tmp\hello-website\mf-watch.yml

mf-watch --project C:\tmp\hello-website --config C:\tmp\hello-website\mf-watch.yml --once
```

This runs exactly what `mf-worker run 0001-hello-page` would (see the Worker Controller's own
[EXAMPLES.md, §3](../worker-controller/EXAMPLES.md), "`run` — execute one specific command", for
the full plan → branch → implement → report walkthrough) — mf-watch just found it via the queue and
spawned it as a subprocess instead of you typing the command by hand. Three ready commands means
three sequential runs; a `--once` invocation drains every `ready` command it finds in one pass,
one at a time, same as `mf-worker run-all`.

Inspect the result exactly as you would after a manual `mf-worker` run — git history, the plan/
report files, `.magnaflow/<id>/` evidence — plus mf-watch's own diary:

```powershell
git log --oneline main
cat .magnaflow\mf-watch.log
```

## 4. Daemon vs. `--once` + scheduler

Running with no `--once` starts the poll loop; `Ctrl+C` shuts it down cleanly (finishes the
current poll, releases the lockfile):

```powershell
mf-watch --project C:\tmp\hello-website --config C:\tmp\hello-website\mf-watch.yml
```

Prefer an OS scheduler instead of a long-lived process? `--once` is built for that — point
Windows Task Scheduler (or cron, on Linux/macOS) at it on whatever cadence you want; the
lockfile still refuses an overlapping run if the previous invocation is somehow still going:

```powershell
schtasks /create /tn "mf-watch hello-website" /sc minute /mo 5 `
  /tr "C:\GIT\magnaflow\tools\mf-watch\src\MagnaFlow.MfWatch\bin\Debug\net10.0\MagnaFlow.MfWatch.exe --project C:\tmp\hello-website --once"
```

Either way, the poll interval adapts on its own (`interval_min_minutes` /
`interval_max_minutes` / `idle_grace_minutes` in `mf-watch.yml`): it stays fast while anything is
happening, and only backs off after real idle time has passed with nothing to do. A scheduler
running `--once` on a fixed cadence doesn't get this — it's the tradeoff for not keeping a
process resident.

## 5. `notify_command` recipes

The template's `{title}` and `{message}` are substituted verbatim before the shell runs it.
Console output (and the log file) always happens regardless — this is the *extra* channel, for
when you're not watching the terminal. Pick something non-blocking; a modal dialog would hang
the poll loop until someone dismisses it.

**Windows**, via the [BurntToast](https://github.com/Windos/BurntToast) module
(`Install-Module BurntToast` once, as your user):

```yaml
notify_command: powershell -NoProfile -Command "New-BurntToastNotification -Text '{title}', '{message}'"
```

**Linux**:

```yaml
notify_command: notify-send "{title}" "{message}"
```

**macOS**:

```yaml
notify_command: osascript -e 'display notification "{message}" with title "{title}"'
```

Trigger one without waiting for a real run, using a dependency-free stand-in that just appends
`{title}`/`{message}` to a file instead of a real notifier — avoid nesting extra quotes inside
`notify_command`; shell + PowerShell quoting stacks fast, so plain redirection is the safe
default:

```powershell
Add-Content C:\tmp\mfwatch-smoketest\proj\mf-watch.yml `
  'notify_command: echo {title} -- {message} >> C:\tmp\mfwatch-smoketest\notifications.log'
```

Then hand-edit a command to look like a previous run crashed mid-way (`status: running`, but
mf-watch itself never dispatched it) — a stale `running` command is exactly the kind of thing
mf-watch surfaces to a human instead of silently ignoring:

```powershell
(Get-Content C:\tmp\mfwatch-smoketest\proj\docs\prompts\0001-cmd-hello.md) `
  -replace 'status: done', 'status: running' |
  Set-Content C:\tmp\mfwatch-smoketest\proj\docs\prompts\0001-cmd-hello.md

mf-watch --project C:\tmp\mfwatch-smoketest\proj --config C:\tmp\mfwatch-smoketest\proj\mf-watch.yml --once
cat C:\tmp\mfwatch-smoketest\notifications.log
# 0001-hello: stale running -- a previous run may have been interrupted; inspect and reset status manually
```

The "don't repeat" guarantee for a stale command is per **process**, not persisted to disk
(v0.1 scope): a long-running daemon won't nag on every subsequent poll, but each fresh `--once`
invocation starts with a clean slate and *will* notify again as long as the command is still
sitting at `running` — running the command above a second time appends a second identical line.
If that repeat notification is unwanted with a `--once`+scheduler setup, that's a signal to fix
the underlying stuck command rather than something to silence.

## 6. `git_sync` — a remote worker machine

`git_sync: true` turns mf-watch into the inbox/outbox for a machine that isn't where commands
get authored — pull before scanning, push after dispatching, so a human (or another mf-watch)
elsewhere just needs to push a `ready` command and wait.

Minimal end-to-end setup with a bare "remote" and two clones (mirrors how the actual feature
was validated). Reuses the `stub-worker.cmd` built in §2 — recreate it first if you skipped
straight here:

```powershell
# A bare repo standing in for the shared remote:
git init --bare C:\tmp\gitsync-demo\remote.git

# "This machine" — where mf-watch runs, git_sync: true. `checkout -b main` (rather than relying
# on whatever init.defaultBranch happens to be) and pointing the bare repo's HEAD at it afterward
# keeps every later clone landing on 'main' instead of git's legacy 'master' default:
git clone C:\tmp\gitsync-demo\remote.git C:\tmp\gitsync-demo\local
cd C:\tmp\gitsync-demo\local
git checkout -b main
git commit -m "seed" --allow-empty
git push -u origin main
git --git-dir=C:\tmp\gitsync-demo\remote.git symbolic-ref HEAD refs/heads/main

@'
git_sync: true
worker:
  command: C:\tmp\mfwatch-smoketest\stub-worker.cmd
'@ | Set-Content mf-watch.yml

# "The other machine" — pushes a ready command upstream:
git clone C:\tmp\gitsync-demo\remote.git C:\tmp\gitsync-demo\other
cd C:\tmp\gitsync-demo\other
mkdir docs\prompts -Force
@'
---
title: Say world
status: ready
attempts: 0
---

## Goal
Do something else useful.
'@ | Set-Content docs\prompts\0002-cmd-world.md
git add -A
git commit -m "seed: 0002-world ready (from other machine)"
git push
```

Now run mf-watch on `local` — one `--once` pulls, runs, and pushes:

```powershell
mf-watch --project C:\tmp\gitsync-demo\local --config C:\tmp\gitsync-demo\local\mf-watch.yml --once
```

```text
git pull: new commits
[0002-world] spawning worker
[0002-world] worker finished (exit 0); status is now 'done'
git push: ok
```

```powershell
git -C C:\tmp\gitsync-demo\remote.git log --oneline main
# ...stub-worker: 0002-world -> done      <- the bookkeeping commit landed upstream, not just locally
# seed: 0002-world ready (from other machine)
# seed
```

A failed pull or push is logged and notified but never stops the poll — whatever's already on
disk is still scanned and run.

## 7. When things go wrong

**Another instance already running** — the lockfile (`.magnaflow/mf-watch.lock`) refuses a
second mf-watch against the same project, whether that's a daemon and a scheduler-triggered
`--once` colliding, or you accidentally starting it twice:

```powershell
mf-watch --project C:\tmp\hello-website --once
# mf-watch: another instance is already running against 'C:\tmp\hello-website'
# (...\.magnaflow\mf-watch.lock is locked)
# exit code 3
```

If it's genuinely stale (mf-watch was killed hard enough to skip cleanup — rare, since the OS
releases the file handle the moment the process dies), delete the lock file by hand.

**`git_sync` enabled but git isn't on this machine** — refused before anything runs:

```text
mf-watch: git is not available on this machine, but git_sync is enabled
# exit code 4
```

**A command stuck at `running`, or the worker exited unexpectedly** — mf-watch re-reads the
command's status after every dispatch; if it isn't `questions`/`done`/`aborted` (or the worker's
exit code wasn't 0/1), it's reported as an error and notified once — same "leave it to the
human" philosophy as a paused `questions` command. Recovery is the same plain-text fix as with
`mf-worker` directly: inspect the logs, reset `status:` by hand, commit.

## 8. Exit codes

```text
0  ran to completion (one poll with --once, or a clean daemon shutdown)
2  usage or configuration error (bad flag, invalid mf-watch.yml)
3  another mf-watch instance already holds the lock for this project
4  git_sync is enabled but git is not available on this machine
```

mf-watch's own exit code says nothing about whether a *dispatched command* succeeded — that's
what notifications and `.magnaflow/mf-watch.log` are for; scripting against the queue itself is
still `mf-worker`'s job (see its own [EXAMPLES.md](../worker-controller/EXAMPLES.md), §9,
"Exit codes — scripting and composition").
