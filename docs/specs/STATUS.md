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

- 0024 install-self-update — ready

## Format problems

none

## Recent spec updates

- 2026-10-05 — 0021: mf-cockpit BUG lines resolved. Markdown links vetted (http/https/mailto/relative only); spec cross-links resolve inside the specs page; follow-up refused (409) unless the parent is done/aborted; config conflict note offers Reload/Overwrite; Add project shows warnings and scaffold output, previews with the server's separator, refuses legacy and flat-root configs with 400, verifies every existing project survives an append, and starts the new project's watcher at once. cockpit/chat.md, specs.md, command.md, config.md, index.md and concepts/machine-config.md updated.
- 2026-10-05 — 0020: mf-worker BUG lines resolved. A re-readied exhausted command resets `attempts:` and replaces its rst (re-run note); a retry without a resumable session resends the full phase prompt, feedback worded per phase; strict CLI parsing (usage errors exit 2 on stderr, help in lane terms); run-all table shows the resulting status; a pln question is a top-level bullet with everything under it. concepts/command-lifecycle.md, concepts/worker-run.md, worker/run.md, worker/next.md, worker/run-all.md updated.
- 2026-10-05 — 0019: mf-watch Ctrl+C no longer cancels the in-flight worker (second Ctrl+C aborts hard); instance lock exclusive on Linux/macOS (`FileShare.None` → exclusive flock, verified broken before); watch/mf-watch.md and concepts/watch-supervision.md#lock-file updated. One narrowed BUG line remains (terminal Ctrl+C reaches the worker via the process group).
- 2026-10-05 — mf-watch.md: terminal Ctrl+C reaching the worker is now an accepted limitation, not a BUG (no 0019B).
- 2026-10-05 — install.ps1 re-run now restarts every watcher that was running (per-project cockpit toggles too), not only start-magnaflow.ps1's -Project; concepts/machine-install.md updated.
- 2026-10-05 — kit description (system.md, README, CLAUDE section, conventions rationale) aligned with the worker: bookkeeping on the invoking branch, no PR by the executor. Lane: 0001 (×2), 0005, 0006, 0008, 0015, 0017 set to done — all verified implemented.
- 2026-10-05 — initial brownfield sync on adoption of the spec kit: 7 cockpit pages, 4 worker subcommands, mf-watch, mf-run, _overview, 8 concepts. All DRAFT; 12 files carry BUG: lines.

0 cosmetic diffs, not listed.
