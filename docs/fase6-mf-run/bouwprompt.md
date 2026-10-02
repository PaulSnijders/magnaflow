# Build prompt — mf-run v0.1

Copy-paste the block below into Claude Code, from the repo root.

---

Build `tools/mf-run/` exactly per the design in
`docs/fase6-mf-run/ontwerp-v0.1.md`. Read that file first; it is the
spec. Also read `tools/worker-controller/` and `tools/mf-watch/` to
match their conventions (project layout, config handling, test
style, .NET 10).

Summary of what you're building: a small cross-platform C# console
tool with four verbs — `start`, `stop`, `restart`, `status`, each
taking an optional service name and `--project <path>` — that
manages the target project's own application processes as configured
in that project's `.magnaflow/config.yml` under `run.services` (list
of `{name, command, args?, workdir?, url?}`). Start spawns each
service detached with stdout/stderr redirected to
`.magnaflow/run/<service>.log` (truncated per start), writes
`.magnaflow/run/<service>.pid` (PID + process start time, plain
text), waits ~2 s and reports failure with a log tail if the process
died immediately; no-op with exit 0 if already running. Stop kills
the entire process tree per PID file, is idempotent, cleans up stale
PID files (start-time mismatch = stale, never kill a recycled PID).
Status prints one line per service and exits 0 only if everything
asked about is running. Start order = list order, stop order =
reverse.

This fase also touches `tools/worker-controller/`: when the target
project has `run.services`, the worker spawns `<run.command> stop
--project <root>` after the plan gate immediately before
implementation (stop failure = refuse to start work, same posture as
the dirty-tree guard: command stays `ready`, no attempt consumed,
notification), and `<run.command> start --project <root>` after a
terminal status — `done` and `aborted` both (start failure = warning
in the rst report with the service log tail, never a retry, never a
status change). A `questions` pause neither stops nor starts.
`run.command` defaults to `mf-run` and is substitutable, same
mechanism as `agent.command`. Process spawn and exit codes only — no
library reference between the tools in either direction.

Requirements beyond the design doc:

- Unit tests in the same style and framework as mf-watch's, with
  fakes for process spawning and the clock. PID-file parsing, the
  start-time staleness guard, config validation (duplicate service
  names, missing command path), start/stop ordering, and the
  worker's two new decision points (stop-failure refusal, terminal
  start including after `aborted`, nothing on `questions`) must all
  be covered without real processes.
- End-to-end validation against the real compiled binaries and a
  disposable project, using a stub service executable that stays
  alive until killed and can spawn a child process of its own:
  start writes the PID file and both `status` and the OS agree it
  runs; stop kills parent *and* child (assert the child is gone —
  this is the locked-port case); stop again is a no-op exit 0; a
  hand-written stale PID file (wrong start time) is cleaned up
  without killing anything; a service whose command exits
  immediately reports a start failure with a log tail. For the
  worker integration, drive a real `mf-worker run` with both
  `agent.command` and `run.command` pointed at stubs and verify the
  stop-before-implement / start-after-terminal call order from the
  stubs' recorded invocations.
- Config read from the target project's `.magnaflow/config.yml`
  (the `run:` section), same parsing approach as the worker
  controller. Every verb must behave sensibly when the section is
  absent: print "no services configured" and exit 0 — a project
  without a `run:` block is the normal console-app case, not an
  error.
- This fase also carries the mf-spec updates along (see the
  design's "Repo hygiene and mf-spec" section): extend the
  runtime-plane block in `docs/mf-spec/system.md` with
  `.magnaflow/run/` (local-only, gitignored — same reasoning as
  `mf-watch.log`/`.lock`), add the ignore line to the kit's adopt
  prompt 0001 and its `.gitignore` guidance in
  `tools/mf-spec/spec-kit/`, and bump the kit version stamp with a
  KIT.md migration note so update prompt 0002 applies it to
  existing target repos. Add the same ignore line to *this* repo's
  own `.gitignore`.
- `tools/mf-watch/` is deliberately untouched: a stop-failure
  refusal leaves the command `ready` with a non-zero worker exit —
  the same shape as the dirty-tree refusal mf-watch already
  reports as an error and re-encounters next poll. Do not add any
  mf-run awareness to the watcher.
- No UI, no daemon mode, no library reference to the other tools.
- If anything in the design doc is ambiguous or contradicts what
  you find in `tools/worker-controller/` or `tools/mf-watch/`, stop
  and ask rather than guess.

When done, write completion notes to
`docs/fase6-mf-run/v0.1-completion-notes.md` in the same style as
`docs/fase4-mf-watch/v0.1-completion-notes.md`: what shipped,
validation performed, deviations, left for later.
