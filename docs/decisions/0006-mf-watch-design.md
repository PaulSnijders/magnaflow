---
date: 2026-07-10
topic: watch
status: accepted
---

# 0006 — mf-watch v0.1 — design (2026-07-10)

> Migrated from `docs/decisions/0006-mf-watch-design.md` on 2026-10-05. Pre-dates the four-section
> decision format; original text unchanged.

The dispatcher role foreseen in v0.1's "deliberately not" list and
`docs/context/2026-07-07-cockpit-multi-project-idea.md`, reduced to its simplest useful form: a small
daemon that watches the prompt lane and feeds the Worker Controller.

```text
Cowork writes cmd (status: ready) in docs/prompts/
      → mf-watch sees it on the next poll
      → spawns mf-worker for that command
      → worker runs (plan gate, questions, report — all unchanged)
      → mf-watch notifies the human when their turn comes
```

## What it does — and all it does

One loop:

1. If `git_sync`: `git pull`.
2. Scan `docs/prompts/` for cmd files with `status: ready`.
3. For each hit, sequentially: spawn `mf-worker` as a separate
   process, wait for it to finish. One run at a time, ever.
4. If `git_sync`: `git push` (the worker's bookkeeping commits).
5. Notify if human attention is needed (see below).
6. Sleep (adaptive interval), repeat.

That is the whole program. It stays this small because the hard parts
already live elsewhere: the frontmatter status **is** the queue
(`questions`/`running`/`done` are not `ready`, so nothing is picked up
twice), and run-level safety (dirty-tree guard, attempts, aborted) is
the worker's job. mf-watch only reads statuses and exit codes.

## Adaptive polling

Config: `interval_min` (default 1 min), `interval_max` (default
15 min), `idle_grace` (default 30 min).

- Poll at `interval_min` as long as anything is happening.
- Only after `idle_grace` with no activity, double the interval each
  empty poll until `interval_max`.
- Any activity — pull brought commits, or a run was executed — resets
  to `interval_min` and restarts the grace window.

"Activity" needs no separate definition or mode switch: the interval
is the mode. Backing off is deliberately slow (grace first), waking up
is instant (one hit resets), because a slow wake-up costs the human
waiting time while slow back-off costs nothing.

> **Added (docs/prompts/0008):** "waking up is instant" held for work
> mf-watch found *by itself*, but not for the human — with the
> interval at `interval_max`, setting a command to `ready` could sit
> unnoticed for 15 minutes. So the sleep is no longer one
> uninterruptible delay: it runs in ~1 s slices that check for an
> empty file at `.magnaflow/mf-watch.wake`, and when it is there
> mf-watch deletes it (that is what makes the wake one-shot), logs one
> line, and starts the next poll immediately. A file, not a signal or
> a socket, because `touch` is then the by-hand equivalent on every
> platform — the invariant below. A wake is deliberately **not**
> activity: it resets neither the interval nor the idle window, since
> asking to look is not the same as finding something. If the poll it
> triggers does find work, the rule above resets both anyway.
> mf-cockpit's `Check now` button is the first writer.

## Local vs remote

Explicit config, no detection: `git_sync: true|false`. On the machine
where Cowork runs, the working copy is already current — no pull/push.
On a remote worker machine, `git_sync: true` makes pull the inbox and
push the outbox. Detecting "am I on the Cowork machine" would be
magic; a setting is honest and debuggable.

## Notifications

Notify at the moments the *human* is at bat — not when work is found
(the daemon handles that itself):

- run finished (`done` / `aborted`)
- questions raised (`questions`)
- errors: worker non-zero exit, pull/push failure

Cross-platform without platform code: `notify_command` in config is a
shell command template with `{title}` and `{message}` placeholders
(Windows: PowerShell toast, Linux: `notify-send`, macOS: `osascript`).
Console output always happens regardless; `notify_command` is the
optional extra channel.

## Observability

Two layers, both plain text:

- Console output while running (the terminal is the v0.1 "window").
- A rolling log file, `.magnaflow/mf-watch.log`: every poll, run
  start/finish, notification, and error — the dispatcher's own diary,
  next to the worker's per-command evidence in `.magnaflow/<id>/`.

Deliberately headless: no tray icon, no GUI. Everything a window
would show (statuses, last activity) is already plain text, and the
cockpit (`docs/context/2026-07-07-cockpit-multi-project-idea.md`) is the designated place to render
it later. Baking UI into the daemon would make state visible only
through the tool — the exact thing the invariant below forbids.

## Guards

- Lockfile in `.magnaflow/` — never two mf-watch instances on the
  same working copy.
- Sequential runs only; no dispatch while a run is in progress.
- A stale `running` status (worker crashed mid-run) is never picked
  up again by mf-watch — it is surfaced via notification and left to
  the human, same philosophy as the `questions` status.

## Invariant: optional, like everything else

mf-watch spawns the `mf-worker` binary as a process; no library
coupling. It knows frontmatter statuses and exit codes, nothing of
internal formats. Everything it automates (pull, run, push, notice)
remains a thing a human can do by hand. If a mechanism can't also be
done manually, the mechanism is wrong — same rule as the Worker
Controller.

## Tech

C# console app, .NET 10 (same toolchain and version as the Worker
Controller), `tools/mf-watch/`. A `--once` flag runs a single poll
cycle and exits — for testing and for anyone preferring an OS
scheduler over the daemon.

> **Updated:** mf-watch.yml (config next to the binary or `--config`)
> was merged into one per-machine `magnaflow.yml`, under a `watch:`
> section, alongside mf-cockpit's `cockpit:` section — see
> `docs/decisions/0012-one-machine-config.md`. The old filename still works next to
> the binary as a deprecated fallback.

## Not in scope (v0.1)

Parallel workers, multi-project support, the cockpit UI,
FileSystemWatcher-based triggering (polling is one code path that
works everywhere), and any scheduling smarter than the backoff above.
