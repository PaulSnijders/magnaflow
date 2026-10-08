---
date: 2026-07-10
topic: watch
status: accepted
---

# 0006 — mf-watch v0.1 — design

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

The worker (decision 0001) runs one command when called, and its
"deliberately not in v0.1" list left polling to a separate dispatcher:
polling never belongs in the controller itself. Without one, every
`ready` command needed a human to start the worker by hand.
`docs/context/2026-07-07-cockpit-multi-project-idea.md` sketched the
larger picture (many projects, a cockpit). The question was the
smallest useful dispatcher.

## Options

1. **A small polling daemon per working copy** that reads frontmatter
   statuses, spawns `mf-worker` as a process, and notifies the human.
2. **Dispatch inside the worker**, as a loop mode. Rejected up front by
   0001: the controller stays one-shot.
3. **Event-driven triggering** (FileSystemWatcher) instead of polling.
4. **A daemon with its own UI** (tray icon, window) for status.

## Measurement

- Smallest program that works: the hard parts already live elsewhere.
  The frontmatter status *is* the queue (`questions`, `running`, `done`
  are not `ready`, so nothing is picked up twice), and run-level safety
  (dirty-tree guard, attempts, aborted) is the worker's job.
- The standing invariant: everything automated (pull, run, push,
  notice) must remain something a human can do by hand, and state stays
  plain text. A mechanism that cannot also be done manually is wrong.
- No coupling: the binary, exit codes and frontmatter, never a library.
- Works the same on every OS without platform code.

## Choice

Option 1. Current behavior: `specs/watch/mf-watch.md`; the files it
shares with the cockpit: `specs/concepts/watch-supervision.md`.

The reasons behind its shape:

- **Polling, not FileSystemWatcher** (option 3): one code path that
  works everywhere, and it must also see work that arrives by `git pull`
  on a remote machine.
- **Adaptive backoff, slow to back off, instant to wake**: a slow
  wake-up costs a human waiting time, a slow back-off costs nothing.
  The interval itself is the mode; there is no separate "active" state.
- **Wake file** (added by prompt 0008): at `interval_max` a command set
  to `ready` could sit unnoticed for minutes. A file, not a signal or a
  socket, because `touch` is then the by-hand equivalent on every
  platform. A wake is deliberately not activity: asking to look is not
  finding something.
- **`git_sync` is an explicit setting, never detected**: detecting "am
  I on the authoring machine" would be magic; a setting is honest and
  debuggable.
- **Notify only when the human is at bat** (finished, questions,
  errors), not when work is found; the daemon handles that itself.
  `notify_command` is a shell template so each OS uses its own notifier
  without platform code.
- **Headless, no tray icon or window** (option 4): everything a window
  would show is already plain text (statuses, the log), and the cockpit
  is the designated place to render it. UI baked into the daemon would
  make state visible only through the tool.
- **A stale `running` is never picked up again**: it is surfaced and
  left to the human, the same posture as `questions`.
- **`--once`** exists for testing and for anyone who prefers an OS
  scheduler over the loop.

Config later moved into the per-machine `magnaflow.yml`
(decision 0012).

Not in scope for v0.1: parallel workers, multi-project support in one
process (later solved as one process per project, see
`specs/concepts/watch-supervision.md`), the cockpit UI, and any
scheduling smarter than the backoff.
