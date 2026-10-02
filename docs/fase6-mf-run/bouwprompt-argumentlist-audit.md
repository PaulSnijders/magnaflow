# Build prompt — ArgumentList/quote audit (mf-watch + worker-controller)

Copy-paste the block below into Claude Code, from the repo root.

---

Audit and, only where reproduced, fix a latent shell-quoting bug in
`tools/mf-watch/` and `tools/worker-controller/`. Read the
"Deviations" section of `docs/fase6-mf-run/v0.1-completion-notes.md`
first — specifically the `ProcessStartInfo.ArgumentList` vs.
`Arguments` finding. That is the bug shape you are hunting.

Background, in short: on Windows, spawning `cmd /d /s /c "<command
line>"` with the command line passed via .NET's `ArgumentList` (or
any argv-style API that re-escapes elements) corrupts embedded
quotes that cmd.exe's `/S`-mode re-parsing needs. mf-run hit this
live: every command line combining embedded quotes with redirection
failed with "the filename, directory name, or volume label syntax
is incorrect" — silently, before the target ever ran. mf-run fixed
it by building the full `/d /s /c "..."` line as one raw string on
`ProcessStartInfo.Arguments`. The completion notes flag that
`CliWrapProcessRunner.RunShellAsync` (used by both mf-watch and the
worker controller) uses `ArgumentList` for its own `cmd /d /s /c`
invocations and may only work today because no configured command
has yet combined embedded quotes with spaces the wrong way.

The exposed surfaces are every user-configurable command that goes
through a shell: mf-watch's `notify_command` (a template whose
`{title}`/`{message}` substitutions can inject quotes — a commit
message or command title containing a `"` is not exotic), and the
worker controller's build/test `command:` entries plus anything
else `RunShellAsync` executes.

Work in this order:

1. **Reproduce before touching anything.** Write failing tests (or
   a minimal standalone repro if the seams don't allow a unit test)
   for both tools, on Windows, using command lines that combine a
   quoted path containing spaces with embedded quotes and, where
   relevant, redirection — e.g. a `notify_command` of
   `powershell -Command "Write-Output '{message}'"` fed a message
   containing a quote, and a build `command:` of
   `"C:\path with spaces\tool.exe" --flag > "out.log"`. Determine
   per call site whether CliWrap's own argument formatter actually
   exhibits the corruption — do not assume it matches
   `ProcessStartInfo.ArgumentList`'s behavior; mf-run's notes
   explicitly leave that open.
2. **If reproduced**: fix each affected tool the way mf-run did —
   compose the full shell invocation as one raw string (each tool
   keeps its own copy of the fix; no shared library, per the
   standing decoupling rule). The fix must not change behavior for
   command lines that worked before: run both tools' full existing
   suites, and keep the new quote-heavy tests green alongside them.
3. **If NOT reproduced** for a call site: change nothing there.
   Keep the passing quote-heavy tests as regression guards, and
   record *why* it is safe (which escaping layer differs from the
   `ArgumentList` path) — a verified "not broken" is a valid and
   valuable outcome of this prompt.
4. Also check the Unix half (`/bin/sh -c`) of the same call sites
   for the equivalent mistake, since the shipped code paths are
   cross-platform — expected safe, but the test cost is trivial.

Requirements:

- No behavior change beyond the fix itself; no refactors of the
  process-runner seams while you're in there.
- Do not touch `tools/mf-run/` (already fixed and validated) or
  `tools/mf-cockpit/` (its chat spawn does not go through a shell;
  confirm that assumption with a quick read and note it, but fix
  nothing there under this prompt).
- If you find a *third* affected surface beyond the ones named
  here, stop and ask before widening scope.

When done, write your findings to
`docs/fase6-mf-run/argumentlist-audit-notes.md`: per call site —
reproduced or not, evidence (the failing/passing test), what was
changed or why nothing was, and any configured-command patterns
users should still avoid.
