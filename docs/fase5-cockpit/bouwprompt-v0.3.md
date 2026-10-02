# Build prompt — mf-cockpit v0.3 (Run & Config)

Copy-paste the block below into Claude Code, from the repo root.

---

Extend `tools/mf-cockpit/` per the design in
`docs/fase5-cockpit/ontwerp-v0.3.md`. Read that file first; it is
the spec. Also read `docs/fase5-cockpit/ontwerp-v0.1.md` (the
invariant, including v0.3's re-scoping of the `.magnaflow/` clause),
`docs/fase6-mf-run/ontwerp-v0.1.md` +
`docs/fase6-mf-run/v0.1-completion-notes.md` (the tool you are
shelling out to — note its actual `status` output and exit-code
contract), and `tools/mf-run/` itself. This fase also touches
`tools/mf-run/` (one flag) — nothing else outside the cockpit.

Summary of what you're building:

1. **`mf-run status --json`** (in `tools/mf-run/`): machine-readable
   status — an array of `{name, running, pid?, url?}`; exit-code
   semantics unchanged; unit tests in mf-run's own suite.
2. **Run card on `project.html`**: per service a running/stopped
   dot, the `url` as a link, an expandable bounded tail of
   `.magnaflow/run/<service>.log`, and start/stop/restart buttons;
   start-all/stop-all in the header; card absent when the project
   has no `run:` services. All status reads and all buttons are
   process spawns of the configured `run.command` (default
   `mf-run`, substitutable — same seam pattern as `chat.command`);
   the cockpit contains **zero** PID or process-tree logic of its
   own. Action responses render mf-run's exit code and output
   inline on the card. `index.html` rows get a `run: k/n` chip,
   fetched asynchronously per row (off the fast summary path, same
   split as the git card). SSE gains a coarse kind `run` for
   changes under `.magnaflow/run/`.
3. **Config page `config.html?p=<name>`**: a parsed read-only
   summary card plus a raw YAML editor for exactly
   `.magnaflow/config.yml` (the endpoint takes no path parameter).
   `GET .../config` returns content + hash; `PUT` takes
   `{content, baseHash}` and enforces, in order: hash match (409),
   YAML parses (400 with the parse error), size cap. Unknown
   top-level keys are a returned warning, never a rejection. On
   success: write + `git add` + `git commit -m "cockpit: edit
   config"`. A banner warns (never blocks) when any command in the
   project is `running`. This is write #5 under the v0.1 invariant
   — update that design doc's action list accordingly.

Requirements beyond the design doc:

- Unit tests in the existing style: config PUT validation order
  (hash drift beats parse error), YAML rejection surfaces position
  info, unknown-key warning path, run-endpoint service-name
  validation (404), and the `run: k/n` aggregation.
- `WebApplicationFactory` integration tests against the disposable
  project fixture, with `run.command` pointed at a stub executable
  (same swap mechanism as `chat.command`'s stub in v0.1): run
  status passthrough incl. the `configured: false` shape, a start
  action returning the stub's output and exit code, a hanging stub
  killed by the timeout, config GET→PUT round-trip preserving
  comments byte-for-byte outside the edited line, 409 on stale
  hash, 400 on broken YAML, and the commit landing with the right
  message.
- Live browser verification (`/browse`) as in v0.1/v0.2: walk the
  run card against a real `mf-run` and a real stub service (start,
  watch the dot flip via SSE, read the log tail, stop), edit the
  config with a YAML syntax error (see the 400 rendered), then a
  valid edit (see the commit in the git card), console clean
  throughout.
- No library reference to mf-run, the worker controller, or
  mf-watch — process spawns and file reads only, as ever.
- No behavior change for projects without a `run:` block — run the
  full existing cockpit suite plus mf-run's suite.
- If mf-run's actual output or exit codes contradict what this
  prompt assumes, or the invariant re-scoping seems to conflict
  with existing code, stop and ask rather than guess.

When done, write completion notes to
`docs/fase5-cockpit/v0.3-completion-notes.md` in the same style as
`v0.2-completion-notes.md`: what shipped, validation performed,
deviations, left for later. Also update
`docs/fase5-cockpit/ontwerp-v0.1.md`'s invariant/action list
(write #5, and the re-scoped `.magnaflow/` clause) and
`docs/fase6-mf-run/ontwerp-v0.1.md`'s "Cockpit (later)" section to
point at this fase as the one that built it.
