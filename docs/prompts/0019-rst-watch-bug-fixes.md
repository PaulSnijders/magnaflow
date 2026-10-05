---
title: "mf-watch: Ctrl+C spares the running worker; verify the lock on Linux"
cmd: 0019-cmd-watch-bug-fixes.md
done: 2026-10-05
summary: "mf-watch's shutdown token no longer cancels the running worker, and the instance lock is now exclusive on Linux/macOS (the bug was real and is fixed). Twelve mf-cockpit tests that already failed on Linux were made OS-aware (test-only). Open: in a terminal, Ctrl+C still reaches the worker directly through the process group; that is the follow-up 0019B, not yet written."
---

# Report: 0019-watch-bug-fixes

## What was done

### Step 1: Ctrl+C spares the running worker

- `WatchLoop.RunOneAsync` no longer passes a cancellation token to the
  worker spawn. Only `worker_timeout_minutes` bounds the worker.
- `PollOnceAsync` checks the token before each dispatch and `break`s;
  it no longer throws. Once a worker has run, later `ready` commands in
  the same poll are skipped. The `git_sync` push still runs, so the
  worker's commits leave the machine.
- The poll/sleep loop moved from `Program.cs` into
  `WatchLoop.RunAsync(scheduler, once, token)` so the loop's exit can be
  unit-tested. Behaviour is unchanged apart from the shutdown semantics
  above.
- New `WatchLoop.IsDispatching`: true while a worker spawn is awaited.
- Ctrl+C handler in `Program.cs`:
  - The first Ctrl+C logs `shutdown requested, finishing current
    poll...`. While a worker runs it also logs `worker still running;
    Ctrl+C again to abort it`. Then it cancels the token.
  - The handler stays registered. A second Ctrl+C logs `second Ctrl+C:
    aborting` and leaves `e.Cancel = false`, so the runtime terminates
    mf-watch.
- New tests:
  - `WatchLoopTests.ShutdownDuringDispatchLetsTheWorkerFinishThenTheLoopExits`:
    the fake worker completes, `done` is notified, the second `ready` is
    not dispatched, the runner's token has `CanBeCanceled == false`, the
    push still runs, and `RunAsync` returns.
  - `WatchLoopTests.ShutdownWhileIdleExitsAtOnce`: a one-minute sleep is
    cut short.
  - `WatchLoopTests.WorkerSpawnIsMarkedAsDispatching`.
  - `FakeProcessRunner` now records the tokens it receives.
- End-to-end on Linux, in a real pty via `script`, with a stub worker
  that ignores SIGINT:
  - One Ctrl+C: both log lines appeared. The stub finished, `done` was
    logged, then `mf-watch stopped` with exit 0. The lock file was
    removed.
  - Two Ctrl+C: `second Ctrl+C: aborting` was logged and mf-watch ended
    at once. The lock file stayed behind, as specced. The stub kept
    running as an orphan, because mf-watch does not kill it and the stub
    ignores SIGINT.

### Step 2: Instance lock on Linux

- **Verified: it reproduces.** On Linux the existing in-process test
  `SecondAcquireIsRefusedWhileFirstHeld` failed: the second `TryAcquire`
  succeeded. .NET turns any share mode other than `None` into a *shared*
  `flock`. The in-process case does not hide it, because `flock` is per
  open file description. This also means `dotnet test` for mf-watch was
  red on Linux before this run.
- Fix: `InstanceLock` uses `FileShare.None` on non-Windows, which gives
  an exclusive `flock`. Windows keeps `FileShare.Read`, which the
  cockpit's `MfWatchLockFile` relies on. The file format is unchanged.
- New test `SecondAcquireFromAnotherProcessIsRefusedWhileHeld` runs the
  real `MagnaFlow.MfWatch.dll --once` in a child process against a held
  lock and expects exit 3.
- `LockFileIsReadableByAnotherHandleWhileHeld` was renamed
  `…ByAnotherReaderWhileHeld`. It reads with a .NET `FileStream` on
  Windows and with `cat` on Unix.
- Regression check: with the share mode temporarily reverted to
  `FileShare.Read`, both the in-process and the child-process tests fail
  on Linux, the child with exit 0. With the fix, all 66 tests pass.

### Specs and docs

- `docs/specs/watch/mf-watch.md`:
  - The lock BUG line is gone, and "Instance lock" describes the lock
    per OS.
  - "Shutdown" describes the new behaviour. It keeps one narrowed BUG
    line, per the qa answer: in a terminal, Ctrl+C reaches the worker
    through the process group.
- `docs/specs/concepts/watch-supervision.md#lock-file` now says exactly
  who can read the file while it is held.
- README.md now describes the per-OS lock and readability. EXAMPLES.md
  §4 describes the new Ctrl+C semantics and the terminal caveat.
- `docs/specs/STATUS.md` has a "Recent spec updates" line.
  `spec_lint`: 0 problems.

### Test-step fix: mf-cockpit tests on Linux (outside this cmd's tool)

The controller's test step also runs mf-cockpit's suite. It had 12
failures on Linux that predate this run and are unrelated to it: the
cockpit's code was not touched, and nothing it reads changed on Linux. I
made test-only changes, with no cockpit behaviour change and no spec
change:

- `MagnaflowYmlAppenderAppendTests` used `C:\x`-style project paths.
  These are relative on Linux, so the appender's `Path.GetFullPath`
  reparse-verify returned `VerifyFailed`. A `RootedPath(name)` helper
  now gives `C:\<name>` on Windows and `/tmp/<name>` elsewhere, and the
  tests keep their coverage on both OSes.
- `ChatE2ETests`, `RunE2ETests` and `NewProjectApiIntegrationTests`
  (8 tests) drive `fixtures/*.cmd` stubs. They now start with
  `if (!OperatingSystem.IsWindows()) return;`, the idiom the repo
  already uses in `ShellQuotingTests`.
- `GitCommitMessageSuggesterTests.Matches_specs_path_case_insensitively_and_handles_backslashes`
  gets the same guard: `Path.GetFileName` splits on `\` only on
  Windows.

All four suites pass on Linux: mf-watch 66, mf-cockpit 297, mf-run 59,
worker-controller 169.

## Open / for the next session

- **Follow-up 0019B (not written, per the qa answer).** A terminal
  Ctrl+C goes to the whole foreground process group; on Windows it goes
  to every process on the console. mf-worker and its agent therefore
  still die on the first Ctrl+C when mf-watch runs in the foreground.
  I verified this in round 1: a parent that swallows Ctrl+C still lost
  its child with exit 130. The fix would be to detach the worker:
  `setsid` on Unix, `CREATE_NEW_PROCESS_GROUP` on Windows.
- Related: the second Ctrl+C does not kill the worker itself. A worker
  that survives SIGINT is orphaned. A real mf-worker does not survive
  it, because it has no Ctrl+C handler. If 0019B detaches the worker,
  the second Ctrl+C must kill the worker tree explicitly.
- I did not observe SIGTERM from `systemctl stop`; it was not tested in
  this run.
- After a hard stop (second Ctrl+C), the lock file is left behind with
  stale content. The spec already covers that case.
- The cockpit's `MfWatchLockFile` doc comment still says mf-watch opens
  with `FileShare.Read`. That is still true on Windows, the only place
  it is called, so I left it alone; it is outside this cmd's tool.
- The 8 `.cmd`-stub cockpit tests now give no coverage on Linux. If
  Linux matters for the cockpit, add `.sh` fixtures and pick the stub per
  OS. The suggester's backslash handling is still effectively
  Windows-only. On Linux, `Docs\Specs\overview.md` yields the whole
  string as the file name, and git never reports such a path there.
  0021 (cockpit bug fixes) could fold both in.
- EXAMPLES.md §7 still tells the user to delete a stale lock file by
  hand. That is harmless but unnecessary, since the next mf-watch
  truncates it. I left it as is.

## Self-answered questions

- **Should a worker that finishes after Ctrl+C still be followed by the
  rest of the poll?** The rest of the poll is skipped, but its push
  runs. The cmd says "no new dispatch", and `git_sync` exists to get
  the worker's result off the machine. I used `break` rather than
  `ThrowIfCancellationRequested`.
- **How does the second Ctrl+C "stop the watcher hard"?** By not
  setting `e.Cancel`, so the runtime's default termination applies. No
  explicit kill of the worker, which keeps the change minimal.
- **How can "the loop exits afterwards" be tested when the loop lived
  in `Program.cs`?** I moved it into `WatchLoop.RunAsync`, with
  unchanged behaviour.
- **How should the lock be made exclusive on Linux while staying
  readable for the cockpit?** `FileShare.None` on non-Windows only.
  `MfWatchLockFile` is called only from `WindowsWatchControl`, and Linux
  liveness uses `systemctl` and `File.Exists`. I rejected
  `FileStream.Lock` (POSIX record locks). It is per-process, so
  in-process acquisitions don't conflict. It is unsupported on macOS.
  And it is released when the process closes any descriptor of the
  file.
- **How do I get the test step green when mf-cockpit's own suite fails
  on Linux, given that this cmd is mf-watch-only?** With test-only
  changes, so cockpit behaviour and specs stay untouched. The appender
  tests got OS-rooted paths so they keep their coverage. Tests built on
  `.cmd` stubs or Windows path semantics got the repo's existing
  `OperatingSystem.IsWindows()` guard.
- **Did the child-process test have to bypass the user's real
  `magnaflow.yml`?** Yes. It passes `--config` pointing at a missing
  file, which means all defaults.
