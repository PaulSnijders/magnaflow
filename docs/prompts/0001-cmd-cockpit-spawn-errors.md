---
title: "cockpit: friendly spawn errors, own log file, config visibility"
status: draft
attempts: 0
created: 2026-07-14
---

## Context

When `cockpit.run.command` (or `chat.command`) points at something that cannot
be started, the Run card's status read throws all the way up and the user gets
a raw `DeveloperExceptionPage` with a `Win32Exception` ("Failed to start a
process with file path 'mf-run'"). Seen in the wild when a stale cockpit
process was still serving with an old in-memory config. Two problems compound
it:

1. A spawn failure of a *configured external command* is treated as an
   unhandled exception instead of a reportable state.
2. The cockpit never tells you **which config file it loaded** or **which
   commands it resolved**, so "why is it running the default?" is
   undiagnosable from the dashboard.

## Task

1. **No endpoint may 500 on a spawn failure.** In
   `Infrastructure/CliWrapProcessRunner.cs`, catch startup failures
   (`Win32Exception` and CliWrap's wrapper around it) and return a normal
   failed `ProcessResult` (non-zero exit code, stderr =
   `could not start '<command>': <OS message>`). Keep the `IProcessRunner`
   contract — callers already handle non-zero exits.
2. **Run card shows the failure inline.** With (1) in place,
   `RunEndpoints.ReadStatusAsync`'s existing error shape
   (`RunStatusDto.Error`) should surface the message. Make sure the message
   includes the command that was attempted, so a wrong/stale config is
   self-explanatory in the UI.
3. **Cockpit log file.** Append one line per notable event to
   `%APPDATA%\MagnaFlow\mf-cockpit.log` (create dir if needed; same location
   convention as the machine config, since a broken project config must still
   be loggable): startup (loaded config path, effective `run.command` and
   `chat.command`, port/bind), config notices, and every spawn failure.
   Bounded size is fine (e.g. simple truncate-at-1MB); KISS, no logging
   framework.
4. **Dashboard visibility.** Extend `/api/config` with the loaded config
   path + effective commands, and show them on `config.html`. Add a bounded
   tail of the cockpit log (reuse `TailReader`) on that same page via
   `GET /api/cockpit/log?tail=N`.
5. **Tests.** Unit: runner returns failed result (not throw) for a
   nonexistent executable. Integration: with `run.command` pointing at a
   nonexistent file, `GET /api/projects/{name}/run` is 200 with the error
   populated; `/api/config` reports the config path.

## Constraints

- Constitution applies: KISS, plain text state, no new dependencies.
- The cockpit stays a window, not an actor: logging its own diary is fine,
  but no behavior changes to what it spawns or writes in project lanes.

## Note

This repo has no `.magnaflow/config.yml` / prompt-lane wiring of its own yet,
so either run this cmd manually in a Claude Code session on `C:\GIT\magnaflow`,
or first wire magnaflow itself into the cockpit/watcher as a second project
(build: `dotnet build` per tool; test: `dotnet test` per tool) and flip this
file to `status: ready`.
