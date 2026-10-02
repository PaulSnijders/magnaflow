---
title: "mf-run: cross-platform service commands (per-OS wrapper resolution)"
cmd: 0004-cmd-mf-run-cross-platform-service-commands.md
done: 2026-07-14
---

## What was done

**New `Runtime/CommandResolver.cs`**: a pure static `Resolve(commandPath, isWindows)` that
implements the resolution rule. A command with an extension is resolved exactly as before — one
exists-check on the literal path, no fallback. An extensionless command tries the bare literal
first, then per-OS suffixes in order: Windows `.cmd`, `.bat`, `.exe`; Unix `.sh`. Returns the
resolved path (or `null`), the full list of paths attempted (for the not-found message), and
whether resolution fell through to a suffix (for the "resolved command to ..." log line). Kept
as a standalone static class, `isWindows` passed in explicitly, so tests can exercise both OS
branches without depending on the host the tests happen to run on.

**`ServiceManager.StartOneAsync`**: now calls `CommandResolver.Resolve` (with
`RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`) instead of a bare `File.Exists` check.
Logs `<service>: resolved command to <path>` only when a fallback suffix was actually used
(nothing new to say when the literal path just worked, same as before). The not-found message
is `command not found: <path>` when only the literal was tried (unchanged wording) and
`command not found: tried <p1>, <p2>, ...` when suffixes were tried too.

**Unix spawn parity (verified, no code change)**: reviewed `SystemProcessSpawner`'s `/bin/sh`
branch against the four requirements — args pass-through (each `service.Args` entry is
individually quoted onto the command line, so the wrapper receives plain argv, not a
re-parsed string), detach (`exec "<path>" ...` replaces the shell process image, so the
spawned PID *is* the target executable, not a lingering shell parent), log redirect (`>
"<logPath>" 2>&1`, same truncate+merge semantics as the Windows branch), kill tree
(`Process.Kill(entireProcessTree: true)` walks the process's descendants same as on Windows,
and there's one fewer layer to walk given `exec` replaced the shell). All four hold by
inspection; no gap needed fixing. One consequence worth documenting: `exec "<path>"` is a raw
`execve()` on the file, so a `.sh` wrapper's executable bit is not optional the way it might be
if mf-run had instead invoked `sh "<path>"` — this repo's design is the former, so the bit is
required (documented in README rather than changed in code, since changing it either way is a
one-line decision with no functional benefit either direction).

**Tests**: new `CommandResolverTests.cs` (10 cases) covers literal resolution, extension-present
never-falls-back (even when a same-named file with another extension exists), Windows order
(`.cmd` > `.bat` > `.exe`, stopping at first match), Windows not-found lists all four paths tried,
Unix `.sh` fallback, Unix not-found lists both paths tried, and a bare extensionless literal that
already exists (no fallback attempted). `ServiceManagerTests.cs` gained three integration-level
cases through `StartAsync`: resolves an extensionless command to whichever wrapper matches the
*actual* host OS running the test and asserts the resolution log line; a `.cmd`-specific request
is never satisfied by a same-named `.exe`; and the not-found message for an extensionless command
lists everything tried. All 59 mf-run tests pass (was 46 before this change).

**`tools/mf-run/README.md`**: new "Cross-platform commands (per-OS wrapper resolution)" section
documenting the resolution rule, the two-wrapper-per-service convention
(`mf-start-web.cmd` + `mf-start-web.sh`, referenced extensionless from config), the executable-bit
requirement for `.sh` wrappers (with the `git update-index --chmod=+x` note, since a `chmod +x`
done outside a commit doesn't survive a fresh clone), and the `"$@"`/`%*` args-forwarding
parity a wrapper pair needs to maintain.

## Decisions

- `CommandResolver` is a separate static class rather than a private `ServiceManager` method —
  the OS branch needs to be parameterized (not read from `RuntimeInformation` internally) for the
  Windows-order and Unix-order tests to run deterministically regardless of which OS actually
  executes the test suite. A pure function with an explicit `bool isWindows` gets that for free.
- Extension detection uses `Path.GetExtension(command) != ""` on the *configured* command string,
  not on the resolved absolute path — same result either way here, but matches where the "has an
  extension" decision conceptually belongs (the user's config value).
- Did not change `SystemProcessSpawner`'s Unix branch to invoke `sh "<path>"` explicitly (which
  would make the executable bit optional) — the task allowed either choice as long as it's
  documented, and changing spawner behavior is out of scope for a resolution-focused prompt with
  no reported problem to fix.

## Scope

`tools/mf-run/` only (`Runtime/CommandResolver.cs` new, `Runtime/ServiceManager.cs`,
`README.md`, `tests/MagnaFlow.MfRun.Tests/CommandResolverTests.cs` new,
`tests/MagnaFlow.MfRun.Tests/ServiceManagerTests.cs`). No dependency additions. No change to
`SystemProcessSpawner`, `Program.cs`, exit-code semantics, or the JSON status contract. The
wozzol2 follow-up (adding the two `.sh` wrappers, dropping extensions from its `config.yml`) is
explicitly out of scope per the prompt's constraints — it lives in a separate repo.
