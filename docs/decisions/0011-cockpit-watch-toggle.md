---
date: 2026-07-15
topic: cockpit, watch
status: accepted
---

# 0011 — mf-cockpit watch toggle — completion notes (2026-07-15)

> Migrated from `docs/decisions/0011-cockpit-watch-toggle.md` on 2026-10-05. Pre-dates the four-section
> decision format; original text unchanged.

A start/stop switch for mf-watch, per project, on `project.html`'s Watcher card. Follow-up to
v0.4 ("Add project"): the adopt flow makes a new project spec-first-ready, but nothing turned its
watcher on without a manual step. Also touches `tools/mf-watch/` (the lock file becomes externally
readable) and `tools/mf-spec/spec-kit/0001-adopt-spec-system.md` (mf-run's `run:` section is now
derived during adopt, alongside this).

## Why two implementations

mf-watch has no per-project "on" concept in its own config — it runs one project per process,
selected by `--project <path>` on the command line. Linux already has real per-project supervision:
`tools/install/install.sh` installs a `mf-watch@.service` systemd template unit, one instance
enabled per watched project path. Windows has none — `start-magnaflow.ps1` starts exactly one
`mf-watch.exe`, hardcoded to a single project set once at install time.

So the toggle is `IWatchControl`, selected once at startup by OS (`Program.cs`):

- **Linux** (`LinuxWatchControl`): shells out to `systemd-escape <path>` then
  `systemctl --user enable/disable/is-active --now mf-watch@<escaped>.service` — no process or PID
  logic of its own, same "shell out, own nothing" posture as `MfRunClient`. Real per-project
  supervision, survives reboot/logout.
- **Windows** (`WindowsWatchControl`): spawns/kills `mf-watch.exe --project <path>` directly via a
  new `IWatchProcessSpawner` seam (mirrors mf-run's own `IProcessSpawner`, duplicated rather than
  referenced — no library coupling between tools, same standing rule). Known gap: an instance
  started this way does not survive a reboot/logout, only the desktop shortcut's single hardcoded
  instance does — until `install.ps1` grows per-project scheduled-task support (not attempted here).

## mf-watch: the lock file becomes externally readable

`WindowsWatchControl` needs to know, for any project, whether mf-watch is genuinely running and
its PID — without owning a second, possibly-inconsistent record of what it started (important
since mf-cockpit itself can restart between a start and a stop click, losing anything held only in
memory). The natural single source of truth is mf-watch's own `.magnaflow/mf-watch.lock`, already
the "is a watcher alive" signal `WatchReader`/`project.html` used — but it was opened with
`FileShare.None` (blocks reads too) and held only the PID as a single value.

Two changes to `MagnaFlow.MfWatch.Runtime.InstanceLock`:

- `FileShare.None` → `FileShare.Read` on acquire. A second `TryAcquire` still fails (it needs write
  access, which a `FileShare.Read`-opened handle doesn't grant a second handle), but a read-only
  opener now succeeds while the lock is held.
- Content is now two lines — PID, then start time (round-trip ISO-8601) — the same shape as
  mf-run's own `.pid` files, so a reader can apply the same PID-reuse guard mf-run does (compare
  the recorded start time against the OS's own start time for that PID, ±2s tolerance) before
  trusting a lock file as "still running" rather than a crash-leftover stale file.

`mf-cockpit`'s new `Watch/MfWatchLockFile.cs` duplicates the read side of this format (not a
library reference — the standing decoupling rule every tool here keeps) and is also what
`WindowsWatchControl.StopAsync` uses to find the PID to kill; no separate PID file of its own.

## What shipped

- `tools/mf-watch/src/MagnaFlow.MfWatch/Runtime/InstanceLock.cs` — externally-readable, two-line
  format (above). 56/56 tests green (was 54), +2 new (external readability, share-mode still
  refuses a second acquire).
- `tools/mf-cockpit/src/MagnaFlow.MfCockpit/`:
  - `Config/CockpitConfig.cs` — `WatchClientConfig` (`cockpit.watch.command`, default `mf-watch`,
    Windows-only).
  - `Watch/MfWatchLockFile.cs`, `Watch/IWatchProcessSpawner.cs` (+`SystemWatchProcessSpawner`),
    `Watch/IWatchControl.cs`, `Watch/WindowsWatchControl.cs`, `Watch/LinuxWatchControl.cs`.
  - `Api/WatchEndpoints.cs` — `GET .../watch` now also reports `running` (via `IWatchControl`, not
    just lock-file presence); new `POST .../watch/start` and `POST .../watch/stop`.
  - `Models/Dtos.cs` — `WatchDto.Running`, new `WatchActionResponseDto`.
  - `Program.cs` — `IWatchControl` registered once, `WindowsWatchControl` or `LinuxWatchControl`
    chosen by `OperatingSystem.IsWindows()`.
  - `wwwroot/project.html` — a Start/Stop button on the Watcher card, next to the existing log tail.
- `tools/mf-spec/spec-kit/0001-adopt-spec-system.md` step 3b — also derives and writes a `run:`
  section (mf-run services) for a project with its own long-running dev/debug process, verified
  against the actual build output rather than guessed; explicitly left out for library/CLI-only
  projects.

## Validation performed

- **Unit tests** (new): `WindowsWatchControlTests.cs` (8) — running/not-running/PID-reuse
  detection against `FakeWatchProcessSpawner`, start-is-a-noop-when-running, stop-is-idempotent,
  stop-never-kills-a-reused-PID. `LinuxWatchControlTests.cs` (5) — `systemd-escape`/`systemctl`
  argument shape against `FakeProcessRunner`, `is-active` exit-code mapping, failure output
  surfaced. `WatchApiIntegrationTests.cs` (7) — endpoint wiring against `FakeWatchControl`
  (`WebApplicationFactory`), 404s on an unknown project. All OS-independent (pure logic against
  fakes) despite `WindowsWatchControl`/`LinuxWatchControl` each being wired up on only one OS in
  production.
- **Full suite green**: mf-cockpit 215/215 (was 195; +20). mf-watch 56/56 (was 54; +2). Debug and
  Release both build clean, 0 warnings.
- **Live end-to-end on Windows** (this dev machine — the Linux path could only be unit-tested, no
  systemd available here): compiled Release `mf-cockpit.exe` + `mf-watch.exe`, a disposable git
  project, `cockpit.watch.command` pointed at the built `mf-watch.exe`.
  - `GET .../watch` on a fresh project: `running: false`, `lockPresent: false`.
  - `POST .../watch/start`: a real `MagnaFlow.MfWatch.exe` process appeared in the OS process list;
    `GET .../watch` then showed `running: true`, `lockPresent: true`, and mf-watch's own log tail
    ("mf-watch starting: project=...", "sleeping 00:01:00").
  - `POST .../watch/stop`: the OS process was gone (`tasklist` confirmed) immediately after;
    `GET .../watch` showed `running: false` (`lockPresent` stayed `true` — the leftover lock file
    from the forced kill, expected: `Process.Kill()` doesn't run mf-watch's own `Dispose`/cleanup).
  - A second `stop` was a no-op success (no process to kill). A following `start` spawned a fresh
    process (new PID), confirmed via `tasklist` and a second "mf-watch starting" line in the log.

## Deviations / interpretations worth flagging

- **No post-start liveness verification.** mf-run's own `start` waits ~2s and reports failure with
  a log tail if the process died immediately; `WindowsWatchControl.StartAsync` does not — it
  reports success as soon as `Process.Start` itself doesn't throw. An immediate config/lock failure
  inside mf-watch (e.g. `--project` pointing somewhere unwritable) would show as "started" until
  the next status poll notices it's not actually running. Scoped out for v1 given effort/complexity
  tradeoff; a natural follow-up if it turns out to matter in practice.
- **No stdout/stderr capture for the spawned process.** Unlike mf-run's `SystemProcessSpawner`
  (which redirects into a per-service log file via shell-level redirection), `WindowsWatchControl`
  spawns mf-watch with no redirection at all — mf-watch already writes everything meaningful to
  `.magnaflow/mf-watch.log` once its own `Logger` initializes, so the only loss is a startup
  failure that occurs before that (a bad `--project` value, an unreadable `magnaflow.yml`) — those
  go nowhere today, visible only as "never became running."
- **Windows reboot/logout persistence gap**, called out above and in the READMEs — a known,
  bounded v1 limitation, not attempted here.

## Not done / left for later

- Windows scheduled-task (or service) registration so a toggled-on project's watcher survives a
  reboot — `install.ps1` still only manages its one hardcoded instance.
- No SSE broadcast on toggle — `project.html` re-fetches watch status directly after a click; an
  event that changes another client's view of the same project's watcher will only refresh on that
  client's own next `watch`-kind event or reload.
- Post-start liveness verification (see Deviations).
