---
title: "mf-watch: Ctrl+C spares the running worker; verify the lock on Linux"
status: ready
created: 2026-10-05
---

## Context

The initial spec sync left two BUG lines in
`docs/specs/watch/mf-watch.md`. Fix both in this run, step by step. If
step 2 cannot be verified on this machine, finish step 1 anyway and
report step 2 in the rst rather than aborting.

## Task

1. **Ctrl+C must not kill the in-flight worker** ("Shutdown").
   - The problem: mf-watch logs "finishing current poll..." and
     README / EXAMPLES.md §4 promise the same. But `WatchLoop.RunOneAsync`
     passes the shutdown token into the mf-worker spawn. Ctrl+C therefore
     kills a running worker and leaves its command at `running`.
   - The fix: separate the two lifetimes. The shutdown token stops the
     loop: no new poll, no new dispatch, and the sleep is cut short. It
     is not passed to the worker spawn; the worker's own
     `command_timeout_minutes` still bounds it.
   - A second Ctrl+C while a worker runs may stop the watcher hard. Say
     so in the log line ("worker still running; Ctrl+C again to abort
     it").
   - Tests:
     - cancelling during a dispatch lets the fake worker complete, and
       the loop exits afterwards;
     - cancelling while idle exits at once.
2. **Instance lock on Linux** ("Instance lock").
   - The problem: `InstanceLock` opens the lock file with
     `FileAccess.ReadWrite, FileShare.Read`. On Linux, .NET maps that to
     advisory `flock` and may take a *shared* lock, so a second watcher
     could acquire it too. This is suspected, not verified.
   - Verify first: add a test that acquires the lock twice, from a child
     process if the in-process case hides it. Run it on Linux and record
     the outcome in the rst.
   - If it reproduces: make acquisition exclusive on every OS. The
     content must stay readable for the cockpit's `MfWatchLockFile`, and
     the file format must not change.
   - If it does not reproduce: keep the code and the test.
3. **Spec-first:**
   - `docs/specs/watch/mf-watch.md`: remove both BUG lines and describe
     the shutdown behaviour and the verified lock behaviour.
   - Adjust `docs/specs/concepts/watch-supervision.md#lock-file` if the
     lock changed.
   - Check that README.md and EXAMPLES.md still match.

## Not in scope

- SIGTERM from `systemctl stop`. Note it in the rst if you observe it.
- The cockpit's Windows Stop (`Stop-Process`).
