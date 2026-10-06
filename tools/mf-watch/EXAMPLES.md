# mf-watch — Examples

A hands-on tour of mf-watch, using the Worker Controller's `hello-website`
example project. Reference: [README.md](README.md),
[mf-watch spec](../../docs/specs/watch/mf-watch.md) and
[decision 0006](../../docs/decisions/0006-mf-watch-design.md). Commands are
PowerShell on Windows; the tool is cross-platform.

- **§2, smoke test:** no agent, no API calls, under a minute. A tiny stub
  stands in for `mf-worker` to check polling, dispatch, status re-read,
  notify and the lockfile.
- **§3, the real thing:** mf-watch dispatches the real `mf-worker`, which
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

`--project` takes any git repository laid out the way `mf-worker` expects
(see the Worker Controller's [EXAMPLES.md, §1](../worker-controller/EXAMPLES.md),
"What a target project must contain"). mf-watch only reads a command's
`status:` and the worker's exit code.

Its own config is the `watch:` section of the per-machine `magnaflow.yml`,
separate from the project's `.magnaflow/config.yml`. Without `--config` it is
looked up next to the binary, then in the user config dir
([lookup order](../../docs/specs/concepts/machine-config.md#lookup-order)),
so one install can watch any project. The examples below pass `--config`
with a file kept outside the watched repo, so it never dirties the worker's
tree. Fields and defaults: [README, Config](README.md#config). Every field
is optional; no file means all defaults.

## 2. Fast smoke test — no agent required

Point `worker.command` at anything that accepts `run --project <root> <id>`
and mf-watch can't tell the difference. This stub marks the command done and
makes a real bookkeeping commit, so §6's `git_sync` push has something to
push. A `.cmd` file runs directly on Windows, so this is the whole "binary":

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
watch:
  worker:
    command: C:\tmp\mfwatch-smoketest\stub-worker.cmd
'@ | Set-Content C:\tmp\mfwatch-smoketest\magnaflow.yml
```

Run one poll cycle:

```powershell
mf-watch --project C:\tmp\mfwatch-smoketest\proj --config C:\tmp\mfwatch-smoketest\magnaflow.yml --once
```

```text
2026-07-10T11:35:07 mf-watch starting: project=C:\tmp\mfwatch-smoketest\proj config=C:\tmp\mfwatch-smoketest\magnaflow.yml once=True
2026-07-10T11:35:07 [0001-hello] spawning worker
2026-07-10T11:35:07 [0001-hello] stub-worker: 0001-hello -> done
2026-07-10T11:35:07 [0001-hello] worker finished (exit 0); status is now 'done'
2026-07-10T11:35:07 mf-watch stopped
```

```powershell
cat docs\prompts\0001-cmd-hello.md   # status: done
cat .magnaflow\mf-watch.log          # the same lines, persisted
```

Run it again. Nothing is `ready`, so it is a no-op: exit 0, no process
spawned, only the start/stop log lines:

```powershell
mf-watch --project C:\tmp\mfwatch-smoketest\proj --config C:\tmp\mfwatch-smoketest\magnaflow.yml --once
```

## 3. The real thing — dispatching mf-worker

Copy the example project; it needs its own git history:

```powershell
Copy-Item -Recurse C:\GIT\magnaflow\tools\worker-controller\examples\hello-website C:\tmp\hello-website
cd C:\tmp\hello-website
git init -b main
git add -A
git commit -m "hello-website: project skeleton + commands"
```

Point mf-watch at the real `mf-worker` binary by full path. The §0 alias
does not resolve in a spawned child process unless it is on `PATH`:

```powershell
@'
watch:
  worker:
    command: C:\GIT\magnaflow\tools\worker-controller\src\MagnaFlow.WorkerController\bin\Debug\net10.0\MagnaFlow.WorkerController.exe
'@ | Set-Content C:\tmp\hello-website.magnaflow.yml

mf-watch --project C:\tmp\hello-website --config C:\tmp\hello-website.magnaflow.yml --once
```

This runs exactly what `mf-worker run 0001-hello-page` would; mf-watch just
found it in the queue. Walkthrough of plan → branch → implement → report:
Worker Controller [EXAMPLES.md, §3](../worker-controller/EXAMPLES.md). One
`--once` drains every `ready` command it finds, one at a time, like
`mf-worker run-all`.

Inspect the result as after a manual `mf-worker` run (git history, plan and
report files, `.magnaflow/<id>/` evidence), plus mf-watch's own log:

```powershell
git log --oneline main
cat .magnaflow\mf-watch.log
```

## 4. Daemon vs. `--once` + scheduler

Without `--once`, mf-watch runs the poll loop. `Ctrl+C` stops it cleanly: no
new poll or dispatch, a sleep ends at once, a running worker finishes its
command, then the lock is released. A second `Ctrl+C` while the worker runs
stops mf-watch hard. Caveat: in a terminal, `Ctrl+C` also reaches the worker
(same console), so a foreground worker dies on the first one. Under systemd
or another service host this does not happen:

```powershell
mf-watch --project C:\tmp\hello-website --config C:\tmp\hello-website.magnaflow.yml
```

To use an OS scheduler instead, run `--once` from Task Scheduler (or cron on
Linux/macOS). The lockfile still refuses an overlapping run:

```powershell
schtasks /create /tn "mf-watch hello-website" /sc minute /mo 5 `
  /tr "C:\GIT\magnaflow\tools\mf-watch\src\MagnaFlow.MfWatch\bin\Debug\net10.0\MagnaFlow.MfWatch.exe --project C:\tmp\hello-website --config C:\tmp\hello-website.magnaflow.yml --once"
```

The daemon adapts its interval (`interval_min_minutes`,
`interval_max_minutes`, `idle_grace_minutes`): fast while anything happens,
backing off only after real idle time. A scheduler on a fixed cadence loses
this; that is the price of no resident process.

## 5. `notify_command` recipes

`{title}` and `{message}` are substituted verbatim before the shell runs the
template. Console and log output always happen; this is the extra channel
for when you're not watching. Pick something non-blocking: a modal dialog
hangs the poll loop until someone dismisses it.

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

These go under `watch:` in `magnaflow.yml`, indented like `worker:`.

To trigger one without a real run, use a stand-in that appends to a file.
Avoid nested quotes in `notify_command` (shell and PowerShell quoting stack
fast); plain redirection is safest. The two leading spaces put the line
inside the `watch:` section of §2's file:

```powershell
Add-Content C:\tmp\mfwatch-smoketest\magnaflow.yml `
  '  notify_command: echo {title} -- {message} >> C:\tmp\mfwatch-smoketest\notifications.log'
```

Then fake a crashed run: set a command to `status: running` without
mf-watch having dispatched it. mf-watch reports a stale `running` command
to the human instead of ignoring it:

```powershell
(Get-Content C:\tmp\mfwatch-smoketest\proj\docs\prompts\0001-cmd-hello.md) `
  -replace 'status: done', 'status: running' |
  Set-Content C:\tmp\mfwatch-smoketest\proj\docs\prompts\0001-cmd-hello.md

mf-watch --project C:\tmp\mfwatch-smoketest\proj --config C:\tmp\mfwatch-smoketest\magnaflow.yml --once
cat C:\tmp\mfwatch-smoketest\notifications.log
# 0001-hello: stale running -- a previous run may have been interrupted; inspect and reset status manually
```

The stale notice is sent once per **process**, not persisted (v0.1 scope).
A daemon won't repeat it on later polls, but each fresh `--once` notifies
again while the command stays `running`: running the command above twice
appends a second line. With a scheduler, fix the stuck command rather than
silence the notice.

## 6. `git_sync` — a remote worker machine

`git_sync: true` makes mf-watch the inbox and outbox of a machine where
commands are not authored: pull before scanning, push after dispatching.
Elsewhere, a human (or another mf-watch) just pushes a `ready` command and
waits.

Minimal setup: a bare "remote" and two clones. Reuses `stub-worker.cmd` from
§2; create it first if you skipped there:

```powershell
# A bare repo standing in for the shared remote:
git init --bare C:\tmp\gitsync-demo\remote.git

# "This machine": runs mf-watch with git_sync: true. `checkout -b main` plus pointing the
# bare repo's HEAD at it makes later clones land on 'main', whatever init.defaultBranch is:
git clone C:\tmp\gitsync-demo\remote.git C:\tmp\gitsync-demo\local
cd C:\tmp\gitsync-demo\local
git checkout -b main
git commit -m "seed" --allow-empty
git push -u origin main
git --git-dir=C:\tmp\gitsync-demo\remote.git symbolic-ref HEAD refs/heads/main

@'
watch:
  git_sync: true
  worker:
    command: C:\tmp\mfwatch-smoketest\stub-worker.cmd
'@ | Set-Content C:\tmp\gitsync-demo\magnaflow.yml

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

Now run mf-watch on `local`. One `--once` pulls, runs and pushes:

```powershell
mf-watch --project C:\tmp\gitsync-demo\local --config C:\tmp\gitsync-demo\magnaflow.yml --once
```

```text
git pull: new commits
[0002-world] spawning worker
[0002-world] worker finished (exit 0); status is now 'done'
git push: ok
```

```powershell
git -C C:\tmp\gitsync-demo\remote.git log --oneline main
# ...stub-worker: 0002-world -> done      <- the bookkeeping commit landed upstream
# seed: 0002-world ready (from other machine)
# seed
```

The pull is `git pull --rebase`: if `local` had unpushed commits of its own
when the other machine pushed, they are replayed on top, and a push rejected
because the remote moved in the meantime gets one rebase-pull and one retry.
A failed pull (a rebase conflict is aborted first, so the tree is unchanged)
means the poll dispatches nothing and does not push. It notifies once, and
later polls with the same local and remote `HEAD` only log
`git pull still failing, ...`. Details:
[git sync](../../docs/specs/watch/mf-watch.md#git-sync).

## 7. When things go wrong

**Another instance already running.** `.magnaflow/mf-watch.lock` refuses a
second mf-watch on the same project, e.g. a daemon and a scheduled `--once`,
or starting it twice by accident:

```powershell
mf-watch --project C:\tmp\hello-website --once
# mf-watch: another instance is already running against 'C:\tmp\hello-website'
# (...\.magnaflow\mf-watch.lock is locked)
# exit code 3
```

A lock file left behind by a hard kill or a second `Ctrl+C` does not block:
the OS released the lock when the process died, and the next instance takes
the file over. Only a live process holds it.

**`git_sync` enabled but no git on this machine.** Refused before anything
runs:

```text
mf-watch: git is not available on this machine, but git_sync is enabled
# exit code 4
```

**A command stuck at `running`, or the worker exited unexpectedly.** After
each dispatch mf-watch re-reads the command's status. If it is not
`questions`/`done`/`aborted`, or the worker's exit code was not 0 or 1, it is
reported as an error and notified once; the human takes over, as with
`questions`. Fix it as with `mf-worker` directly: inspect the logs, reset
`status:` by hand, commit.

## 8. Exit codes

Codes 0, 2, 3 and 4: see [README, Exit codes](README.md#exit-codes).

mf-watch's exit code says nothing about whether a *dispatched command*
succeeded; notifications and `.magnaflow/mf-watch.log` cover that. To script
against the queue, use `mf-worker` (its
[EXAMPLES.md](../worker-controller/EXAMPLES.md), §9, "Exit codes — scripting
and composition").
