# ArgumentList/quote audit — mf-watch + worker-controller (2026-07-14)

Follow-up to `v0.1-completion-notes.md`'s "Deviations" flag: mf-run hit a Windows-only bug where
`ProcessStartInfo.ArgumentList` re-escapes each argv element, and when that element is itself a
`cmd /d /s /c "<command line>"` string containing embedded quotes, the extra escaping corrupts the
quotes cmd.exe's `/S`-mode re-parsing needs — every redirected spawn failed with "the filename,
directory name, or volume label syntax is incorrect", silently, before the target ever ran. This
audit checks whether `CliWrapProcessRunner.RunShellAsync` (shared code, byte-identical in
mf-watch and worker-controller) has the same problem.

## Result: reproduced in both tools, on Windows, and fixed

CliWrap's array-form `WithArguments(["/d", "/s", "/c", commandLine])` turned out to apply its own
argv-style escaping per element — not meaningfully different from `ProcessStartInfo.ArgumentList`
for this purpose. A direct three-way comparison (`ArgumentList` vs. CliWrap's array `WithArguments`
vs. a raw unescaped string) against two command-line shapes showed CliWrap failing identically to
the known-broken `ArgumentList` baseline in both cases:

1. **Quoted path with spaces + redirection** — `"<dir with spaces>\tool.cmd" > "<log with
   spaces>" 2>&1`. Both `ArgumentList` and CliWrap's array form: exit 1, stderr `De syntaxis van de
   bestandsnaam, mapnaam of volumenaam is onjuist.` (Dutch locale for the exact error text named in
   the completion notes). The raw/unescaped forms: exit 0, log file created with expected content.
2. **Embedded quotes inside an already-quoted argument** — e.g.
   `powershell -Command "Write-Output 'text with an embedded "quote"'"`. Same split: both
   `ArgumentList` and CliWrap's array form fail with a PowerShell parser error
   (`UnexpectedToken`); the raw/unescaped forms succeed.

### Fix applied

Both `tools/mf-watch/src/MagnaFlow.MfWatch/Infrastructure/CliWrapProcessRunner.cs` and
`tools/worker-controller/src/MagnaFlow.WorkerController/Infrastructure/CliWrapProcessRunner.cs`
(kept as separate copies each, no shared library, per the standing decoupling rule) now use
CliWrap's **single-string** `WithArguments(string)` overload for the Windows branch of
`RunShellAsync`, instead of the array form:

```csharp
? Cli.Wrap("cmd.exe").WithArguments($"/d /s /c \"{commandLine}\"")
```

This passes the whole `/d /s /c "..."` line through unescaped — confirmed by the same comparison
harness to behave identically to mf-run's `ProcessStartInfo.Arguments` fix (exit 0, correct output,
in both scenarios above) — while staying inside CliWrap's `Command` API, so `ExecuteAsync`'s
piping/timeout/cancellation plumbing (shared by both the executable and shell code paths) needed no
changes. This is **not** a mf-run-style drop to raw `ProcessStartInfo`; it's the equivalent fix
expressed through the API these tools already use.

The Unix branch (`Cli.Wrap("/bin/sh").WithArguments(["-c", commandLine])`) was left untouched — see
"Unix half" below for why it's safe.

### Regression tests

Both tools gained `tests/.../ShellQuotingTests.cs`, exercising the real OS shell (no fakes) against
`CliWrapProcessRunner.RunShellAsync` directly:

- **mf-watch**: a notify_command-shaped repro (`powershell -Command "Write-Output '{message}'"`
  fed a message containing an embedded quote), a quoted-path-with-spaces-plus-redirection repro,
  and one exercising the real call site end-to-end through `ShellNotifier.NotifyAsync`.
- **worker-controller**: a build/test `command:`-shaped repro
  (`"<dir with spaces>\tool.cmd" --flag > "<log>" 2>&1`) and an embedded-quotes-in-arguments repro.
  Tested directly against `CliWrapProcessRunner`, which is faithful to the real call site —
  `TaskRunner.RunSequentiallyAsync` forwards a configured `command:` string to `RunShellAsync`
  unmodified, with no intermediate escaping of its own.

Verified each test actually exercises the bug: reverted the fix locally, confirmed all of the
above failed with the same error signatures shown above, then restored the fix — all green again.
Both tools' full suites pass alongside the new tests: **42/42** (mf-watch), **137/137**
(worker-controller).

## Unix half

`Cli.Wrap("/bin/sh").WithArguments(["-c", commandLine])` was checked and left unchanged — expected
safe by construction, not just by absence of a failing test. The Windows bug is specific to Windows
process creation: a Win32 child process receives one flattened command-line *string*, so the parent
must encode argv into text, and `cmd.exe /S` then re-parses that text — two textual round-trips
where escaping can go wrong. POSIX process creation passes `argv` as an actual array of exact byte
strings (`execve`), with no text-based re-parsing layer for the OS to get confused by; whatever
CliWrap's array-form escaping does to the `commandLine` element, it arrives at `/bin/sh -c` as one
exact string either way. There is nothing for the corruption to attach to.

Added anyway, at trivial cost, as regression guards for both tools
(`RunShellAsync_unix_quoted_path_with_spaces_plus_redirection_would_succeed`): gated to run only
when `!OperatingSystem.IsWindows()`, so they no-op on this Windows dev machine and actually execute
wherever these suites next run on a Unix CI/dev box.

## mf-cockpit: confirmed out of scope, unchanged

`ClaudeChatRunner.RunAsync` (`tools/mf-cockpit/src/MagnaFlow.MfCockpit/Chat/ClaudeChatRunner.cs`)
calls `processRunner.RunExecutableAsync(config.Command, args, ...)` directly against the `claude`
executable — no `cmd.exe`/`/bin/sh` shell in between. Confirmed further: mf-cockpit's own
`IProcessRunner` interface doesn't even declare a `RunShellAsync` method, so there's no shell-quoting
surface there at all. Nothing touched, per the prompt's scope.

## Configured-command patterns to still avoid

The fix removes the corruption, but two independent hazards remain and are worth naming for anyone
writing `notify_command` or a build/test `command:`:

- **Unbalanced quotes** in a configured command are still the author's responsibility — the fix
  makes `cmd.exe` see exactly what was written, it doesn't validate it.
- **Windows vs. Unix shell syntax differs** (`%VAR%` vs. `$VAR`, `2>&1` ordering, `type`/`cat`, path
  separators) — a `command:`/`notify_command` meant to be cross-platform still needs per-OS
  variants or a wrapper script; this audit only fixed how the string is *handed to* the shell, not
  what the shell does with it.
