# Build prompt — mf-cockpit v0.1

Copy-paste the block below into Claude Code, from the repo root.

---

Build `tools/mf-cockpit/` exactly per the design in
`docs/fase5-cockpit/ontwerp-v0.1.md`. Read that file first; it is the
spec. Also read `tools/worker-controller/` and `tools/mf-watch/` to
match their conventions (project layout, config handling, test style,
.NET 10), and these for the file formats you will render:
`docs/mf-spec/system.md` (lane layout),
`docs/fase2-worker-controller/v0.2-completion-notes.md` (cmd statuses,
evidence files), `docs/fase4-mf-watch/ontwerp-v0.1.md` (watcher log,
lockfile).

Summary of what you're building: an ASP.NET Core minimal API +
static-file web app (Kestrel, no SPA framework, no CDN dependencies)
that renders one or more MagnaFlow projects read-only: the prompt
lane (`docs/prompts/NNNN-{cmd,pln,qa,rst}-*.md` grouped by id, with
frontmatter status), per-command evidence tails
(`.magnaflow/<id>/claude.log`, `build.log`, `test.log`,
`session.yml`), the watcher tail and lock
(`.magnaflow/mf-watch.log`, `.magnaflow/mf-watch.lock`), the spec
tree (`docs/specs/`), and read-only git info. Live updates via
FileSystemWatcher → debounced SSE; clients re-fetch. Pages:
cockpit overview with cross-project attention list, project view,
command drill-down, spec browser, and a chat page that spawns
headless Claude Code read-only per message (resume via session_id)
and can escalate an answer into a new `status: draft` cmd file. The
only writes anywhere: create draft cmd, flip draft→ready — both
followed by an immediate `git add` + `git commit`. Dark theme per
the palette and images in the design doc.

Requirements beyond the design doc:

- Unit tests in the same style and framework as mf-watch's, with
  fakes for git, process spawning (the chat), the clock, and the
  filesystem where practical. The lane scanner, tail reader, path
  safety, draft-numbering, and draft→ready transition logic must be
  covered.
- API integration tests via `WebApplicationFactory` against a
  disposable project fixture (a temp dir seeded with lane files,
  evidence, and a git repo), covering: project summary counts,
  command detail aggregation, watch tail, specs path-escape
  rejection (expect 400/404, never file content from outside the
  root), draft creation picks the next free NNNN, ready-flip
  refuses any status other than draft (409), and SSE emits after a
  lane file change.
- Chat end-to-end against a stub agent executable (same
  swap-the-command mechanism the worker's and mf-watch's validation
  used): configured `chat.command` pointing at a stub that echoes a
  fixed JSON reply with a session_id; verify streaming reaches the
  client, the session_id is reused on the second message, and the
  timeout kills a hanging stub.
- Config file next to the binary or via `--config`, YAML, every
  field defaulted, same parsing approach as mf-watch.
- Copy `docs/images/magnaflow-banner-wide.png`,
  `magnaflow-logo.png`, and `magnaflow-logo.ico` into
  `wwwroot/assets/` as part of the project (build-time content
  files, not runtime reads from docs/).
- No library reference to the worker controller or mf-watch;
  process spawn and file reads only.
- If anything in the design doc is ambiguous or contradicts what
  you find in `tools/worker-controller/` or `tools/mf-watch/`, stop
  and ask rather than guess.

When done, write completion notes to
`docs/fase5-cockpit/v0.1-completion-notes.md` in the same style as
`docs/fase4-mf-watch/v0.1-completion-notes.md`: what shipped,
validation performed, deviations, left for later.
