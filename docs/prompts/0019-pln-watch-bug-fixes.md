# Plan: 0019-watch-bug-fixes

## Step 1: Ctrl+C spares the running worker

Planned approach (the fix the cmd prescribes):

- `WatchLoop.RunOneAsync` stops passing the shutdown token to
  `RunExecutableAsync`. `PollOnceAsync` still checks it before every
  dispatch (no new dispatch), and `Program.cs` still checks it before the
  sleep and passes it into `WakeFile.SleepAsync` (no new poll; the sleep
  is cut short). The worker is bounded only by `worker_timeout_minutes`.
- `Program.cs`: a `dispatching` flag (set around each worker spawn
  through a callback/property on `WatchLoop`). On the first Ctrl+C:
  `shutdown requested, finishing current poll...`, plus
  `worker still running; Ctrl+C again to abort it` when a worker is
  running. The handler stays registered. A second Ctrl+C sets
  `e.Cancel = false`, so the runtime ends the process hard.
- Tests (fake `IProcessRunner`): cancelling during a dispatch lets the
  fake worker finish, the outcome is still logged and notified, the
  token never reaches the runner, and later `ready` commands are not
  dispatched; cancelling while idle makes the sleep end at once.
- Spec `docs/specs/watch/mf-watch.md` "Shutdown": remove the BUG line,
  describe the behaviour above. Then check README.md and EXAMPLES.md §4.

Finding that the cmd did not foresee (verified on Linux in a pty):
a Ctrl+C typed in a terminal goes to the **whole foreground process
group**, not only to mf-watch. I tested a parent that swallows Ctrl+C
(`e.Cancel = true`) and passes no cancellation token to its child, a
.NET process like mf-worker. The child still died with exit 130 on the
first Ctrl+C. Windows consoles do the same: Ctrl+C goes to every
process attached to the console. mf-worker has no Ctrl+C handler, and
the agent CLI it spawns gets the signal too. So separating the tokens
alone is necessary but **not sufficient**. In a terminal, Ctrl+C still
kills the in-flight worker and leaves its command `running`. Under
systemd (no terminal) the issue does not arise.

## Step 2: Instance lock on Linux

Verified, it reproduces: on Linux the existing in-process test
`InstanceLockTests.SecondAcquireIsRefusedWhileFirstHeld` already fails,
because the second `TryAcquire` succeeds. .NET takes a shared `flock`
for `FileShare.Read`, and the in-process case does not hide it. That
test also means `dotnet test` is red on Linux today.

Planned fix, resolved from the code (no question):

- Use `FileShare.None` on non-Windows. That gives an exclusive `flock`
  on Linux and macOS, in-process too. Keep `FileShare.Read` on Windows,
  where the share mode itself is the exclusive-for-writers lock and the
  cockpit's `MfWatchLockFile` reads the content.
- On Unix `flock` is advisory, so `cat` and other plain readers still
  read the file. Only another .NET `FileStream` is refused, because .NET
  takes `LOCK_SH` when it opens a file. The only .NET reader,
  `MfWatchLockFile`, is called solely from `WindowsWatchControl`. Linux
  liveness goes through `systemctl`, and `lockPresent` is `File.Exists`.
  The file format does not change.
- I rejected `FileStream.Lock` (POSIX record lock). It is per-process,
  not supported on macOS, and released when the process closes any
  descriptor of the file.
- Tests: keep the in-process test. Add a child-process test that runs
  the built `MagnaFlow.MfWatch.dll --once` against a held lock and
  expects exit 3. Make the "readable while held" test OS-aware: a .NET
  reader on Windows, `cat` on Unix.
- Specs: `mf-watch.md` "Instance lock" drops the BUG line and describes
  the per-OS lock. `watch-supervision.md#lock-file` gets "readable while
  held" made precise: plain readers on Unix, any reader on Windows.

## Open questions (this round)

- **How far should step 1 go, given that a terminal Ctrl+C reaches the
  worker directly through the process group?**
  - (A) Do only the prescribed token separation in this run. Keep a
    narrowed BUG line in "Shutdown": "a foreground Ctrl+C still reaches
    mf-worker and its agent through the terminal's process group". Fix
    it in a follow-up cmd `0019B`.
  - (B) Also detach the worker from the console in this run. On Unix,
    spawn it under `setsid`. That needs the `setsid` binary, which
    util-linux ships but stock macOS lacks. On Windows, use
    `CREATE_NEW_PROCESS_GROUP`, which needs a P/Invoke `CreateProcess`
    instead of CliWrap and cannot be verified on this machine. A second
    Ctrl+C would then have to kill the worker tree explicitly, because a
    detached worker no longer dies with the watcher.

  My recommendation is (A). The token separation is still needed, and
  it already fixes the `systemctl stop` and Stop paths' half of the
  problem. (B) changes how the worker is spawned on both OSes and needs
  its own Windows verification.
