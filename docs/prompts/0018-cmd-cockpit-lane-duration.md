---
title: "mf-cockpit: a Duration column in the lane"
status: done
created: 2026-09-21
---

## Context

The Lane card on `project.html` shows Id / Title / Status / Attempts /
Branch, but not how long a command took. Was it 0:23 or 25 minutes?
Today you have to open `claude.log` to find out.

The information is already on disk, no worker change needed:

- **Start**: the first line of `.magnaflow/<id>/claude.log` is always a
  header the worker writes before anything else
  (`AttemptLogWriter`): `=== plan — 2026-09-10T14:03:22 ===` (older
  logs: `=== attempt 1/3 — … ===`). The timestamp is local time, no
  offset.
- **End**: `EvidenceReader.LastActivityUtc` — the last write among
  claude/build/test.log. It already exists for stale-`running`
  detection.

## What to build

1. `EvidenceReader`: a method that returns a command's duration, or
   null when there is no `claude.log` or its first line is not a
   parseable header. Read **only the first line** — this runs for every
   lane command on every refresh and the log can be megabytes (same
   reasoning as `RstSummary`). Start is local time, so compare it with
   the local last-write time, not UTC.
   - status `running`: end = now (`IClock`), so the value grows with
     each lane refetch.
   - any other status: end = last activity.
2. `LaneItem` / `CommandSummaryDto`: carry it as `DurationSeconds`
   (`int?`).
3. `project.html`: a `Duration` column between Attempts and Branch.
   Format `m:ss` under an hour (`0:23`, `5:07`), `h:mm:ss` above.
   Null renders an empty cell. Remember the two `colspan="6"` rows.
4. xUnit tests for the reader: header parsed (both header shapes), no
   log → null, garbage first line → null, running uses the clock.

## Not in scope

- A command that paused on `questions` and was resumed counts the
  human waiting time too (first header → last write). That is rare
  enough to accept; exact per-phase timing would mean scanning the
  whole log.
- No worker change, no new field in any file format.
- No duration on `command.html` or `index.html`.
