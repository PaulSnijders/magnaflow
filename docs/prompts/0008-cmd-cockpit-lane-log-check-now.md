---
title: "mf-cockpit: newest-first lane, short watcher log tail, and a Check now button"
status: draft
attempts: 0
created: 2026-08-17
---

## Context

`project.html` was built for short projects. On a project with many
commands it now fails at the one thing it is for — showing what is
happening *now*:

1. The lane renders oldest → newest, so the command you are actually
   working on is at the bottom of a long list.
2. The Watcher card renders the whole `mf-watch.log` tail, so the most
   recent line is again at the far bottom, behind a scroll.
3. The Watcher card can start and stop mf-watch, but when the daemon has
   backed off to `interval_max` (15 min) there is no way to say "look
   now" short of stopping and starting it.

Items 1 and 2 are `wwwroot/project.html` only. Item 3 needs a wake
mechanism in mf-watch — its sleep is currently one uninterruptible
delay.

## Task

1. **Lane newest-first, 10 at a time.** Keep the existing ordinal id
   sort (`0005 < 0005B < 0006`) and render it reversed: highest id on
   top. Show the first 10; below them a `+ N older` button that reveals
   the rest in one click. No button when the lane holds 10 or fewer.

   The expanded/collapsed flag is page state that survives a
   `Cockpit.live` refetch — re-rendering the lane must not silently
   collapse a list the human just expanded.

   Slice at render time only. The full item list still feeds the
   status-cue transition detection from `0007`, which must keep chiming
   for a command that is currently hidden.

2. **Watcher log: last 10 lines + `load all`.** Render only the last 10
   lines of the tail the Watcher card already fetched, in their normal
   chronological order (like `tail` — a log read backwards is worse, and
   at 10 lines nothing needs scrolling). A `load all` button below shows
   the rest. Client-side only: `GET .../watch` is unchanged and the
   button reveals lines that were already fetched. Same
   survives-a-refetch rule as item 1.

3. **`Check now`.** A wake file, `.magnaflow/mf-watch.wake`:

   - **mf-watch**: replace the single sleep with short slices (~1 s)
     that check for the file; when it exists, delete it and start the
     next poll immediately. Deleting it is what makes the wake one-shot.
     Log one line for it. A wake is *not* activity — it must not reset
     the backoff interval or the idle window; if the poll finds work,
     the existing rule resets them anyway.
   - **mf-cockpit**: `POST /api/projects/{name}/watch/check-now` writes
     the empty file. Deliberately not on `IWatchControl` — a file write
     is identical on both platforms, so this endpoint needs no
     Linux/Windows split at all.
   - **`project.html`**: a `Check now` button next to Start/Stop,
     disabled while the watcher is not running (nothing would consume
     the file), refetching watch status after the click exactly like the
     existing buttons do.
   - Tests: the wake file is detected and deleted (mf-watch, over a temp
     dir); the endpoint writes it and 404s on an unknown project
     (`WatchApiIntegrationTests` pattern).
   - Document the wake file in `tools/mf-watch/README.md` and add a
     short paragraph to `docs/fase4-mf-watch/ontwerp-v0.1.md` — the
     sleep is design-level behaviour and it is changing.

## Verify live

On a project with more than 10 commands: the newest command is the top
row without scrolling, `+ N older` reveals the rest and stays open
across a lane change. The Watcher card shows 10 log lines, `load all`
shows the whole tail. With the watcher running and backed off to 15 min,
`Check now` produces a fresh poll line in the log within seconds, and
the `.wake` file is gone afterwards.

Not in scope: paging the lane server-side, a line-count setting for
either list, reversing the log, filtering the lane by status, waking a
*stopped* watcher, and `install.ps1`'s Windows persistence gap.
