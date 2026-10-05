# Spec status

Generated: 2026-10-05 by /spec-drift

## Missing specs

none

## Specs without a page

none

## Stale specs (code ahead — enforcement gap)

none

## Spec ahead (implementation pending)

none

## Hotfix debt

none

## Open prompts

- 0019 watch-bug-fixes — ready
- 0020 worker-bug-fixes — draft
- 0021 cockpit-bug-fixes — draft

## Format problems

none

## Recent spec updates

- 2026-10-05 — 0019: mf-watch Ctrl+C no longer cancels the in-flight worker (second Ctrl+C aborts hard); instance lock exclusive on Linux/macOS (`FileShare.None` → exclusive flock, verified broken before); watch/mf-watch.md and concepts/watch-supervision.md#lock-file updated. One narrowed BUG line remains (terminal Ctrl+C reaches the worker via the process group).
- 2026-10-05 — install.ps1 re-run now restarts every watcher that was running (per-project cockpit toggles too), not only start-magnaflow.ps1's -Project; concepts/machine-install.md updated.
- 2026-10-05 — kit description (system.md, README, CLAUDE section, conventions rationale) aligned with the worker: bookkeeping on the invoking branch, no PR by the executor. Lane: 0001 (×2), 0005, 0006, 0008, 0015, 0017 set to done — all verified implemented.
- 2026-10-05 — initial brownfield sync on adoption of the spec kit: 7 cockpit pages, 4 worker subcommands, mf-watch, mf-run, _overview, 8 concepts. All DRAFT; 12 files carry BUG: lines.

0 cosmetic diffs, not listed.
