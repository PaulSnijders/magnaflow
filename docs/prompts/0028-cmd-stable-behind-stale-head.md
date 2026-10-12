---
title: "Cockpit stable row: Behind reads HEAD with the run data, not from the Git card"
status: ready
created: 2026-10-12
---

## Context

Follow-up to 0027 (stable instance). After the worker finishes a cmd it
commits, pushes and runs `mf-run promote`. The stable row on the project
page then shows the new `sha` (the `run` event for `state.yml` refetches
the Run card) but still marks it **behind**. A page reload clears the
mark: everything was in fact up to date.

Cause: `project.html` compares the stable `sha` with `gitHead`, which is
set only by `loadGit()` (`commits[0].hash`). The Git card is deliberately
not on the SSE fast path, so `gitHead` still holds the HEAD from before
the run's commit.

The spec is already ahead: `docs/specs/cockpit/project.md#run`. Behind
compares with a HEAD that comes with the run data.

## Task

1. **Server** (`Api/RunEndpoints.cs`): when the project has `run.stable`,
   add the checked-out HEAD sha to the run response, read with one
   `git rev-parse HEAD` through the existing git/process seam. Keep it
   out of the few-seconds status cache, or drop it from the cache together
   with the stable entry. A failed read gives `null`, and then no Behind
   mark, never an error on the card. Add the field to the DTO in
   `Models/Dtos.cs` as an additive, nullable field. Without `run.stable`
   the response stays byte-for-byte the same and git is not spawned.
2. **Page** (`wwwroot/project.html`): Behind compares `s.sha` with that
   field. `gitHead` and the stable row's re-render from `loadGit()` go
   away, unless the Git card itself still needs them.
3. **Tests** (xUnit): the run response carries the head when `run.stable`
   is set; it does not when `run.stable` is absent, and git is not called;
   a failing rev-parse gives `null`.

## Must not change

- The Git card stays off the SSE fast path (`project.md#live-updates`).
- Projects without `run.stable`: same API response, same card.

## Verify

- `dotnet test` for mf-cockpit.
- In headless Chrome against a scratch project with `run.stable`: promote,
  make a new commit, and the next `run` refetch shows **behind** without
  a reload. Promote again, and **behind** disappears without a reload.
- After the run, by hand (needs `tools/install/install.ps1`): on
  triferto-forecast, run a small cmd. After `done` the stable row shows
  the new sha without **behind**, and without a reload.
