# Plan: 0019-watch-bug-fixes (round 2, after qa)

The qa answer is (A): token separation only, plus a narrowed BUG line.
Step 2 stays as planned.

## Step 1: Ctrl+C spares the running worker

- `WatchLoop.RunOneAsync` no longer passes the shutdown token to
  `RunExecutableAsync`. The worker is bounded only by
  `worker_timeout_minutes`.
- `PollOnceAsync` still checks the token before each dispatch, so no new
  dispatch starts. Once a worker has run, `ready` commands later in the
  same poll are skipped.
- `Program.cs` still checks the token before the sleep and passes it to
  `WakeFile.SleepAsync`, so there is no new poll and the sleep ends at
  once.
- `WatchLoop` exposes `IsDispatching`, which is true while a worker
  spawn is awaited.
- First Ctrl+C:
  - Log `shutdown requested, finishing current poll...`.
  - If a worker runs, also log
    `worker still running; Ctrl+C again to abort it`.
  - Set `e.Cancel = true`, cancel the token, and keep the handler.
- A second Ctrl+C sets `e.Cancel = false`, so the runtime ends the
  process hard. It logs `second Ctrl+C: aborting` first.
- Tests use the fake `IProcessRunner`:
  - Cancelling during a dispatch: the fake cancels the token while
    "running" and then writes `done`. The outcome is still logged and
    notified. The runner never received a cancellable token. A second
    `ready` command is not dispatched, and the poll returns normally.
  - Cancelling while idle: `WakeFile.SleepAsync` with a cancelled token
    over a long interval ends at once.
- Spec "Shutdown" describes this. A narrowed BUG line stays: a
  foreground Ctrl+C in a terminal still reaches mf-worker and its agent
  through the process group. This was verified on Linux in a pty (child
  exit 130), and Windows consoles behave the same. Under systemd it does
  not arise.
- Align the shutdown wording in README.md and EXAMPLES.md §4. The rst
  names `0019B` as the open follow-up, but I will not write it.

## Step 2: Instance lock

- Verified on Linux: the existing in-process
  `SecondAcquireIsRefusedWhileFirstHeld` fails, because the second
  acquire succeeds. The cause is the shared `flock` that .NET takes for
  `FileShare.Read`.
- Fix: `FileShare.None` on non-Windows, which gives an exclusive
  `flock`. Keep `FileShare.Read` on Windows, where `MfWatchLockFile`
  reads the content. The format is unchanged.
- On Unix the lock is advisory. Plain readers such as `cat` still work;
  only .NET readers are refused. The cockpit reads the content only on
  Windows (`WindowsWatchControl`), and `lockPresent` is `File.Exists`.
- Tests:
  - Keep the in-process double-acquire test.
  - Add a child-process test: `dotnet MagnaFlow.MfWatch.dll --once
    --project <tmp> --config <missing file>` while the lock is held
    exits 3.
  - Make the "readable while held" test OS-aware: a .NET reader on
    Windows, `cat` on Unix.
- Specs: `mf-watch.md` "Instance lock" drops the BUG line and describes
  the lock per OS. `watch-supervision.md#lock-file` makes "readable
  while held" precise.

## Records

- `0019-rst-watch-bug-fixes.md`:
  - What changed.
  - The Linux verification outcome for both steps.
  - The pty finding.
  - `0019B` as the open follow-up.
  - That SIGTERM from `systemctl stop` was not observed in this run.
