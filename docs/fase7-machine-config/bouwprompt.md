# Build prompt — one machine config: magnaflow.yml (fase 7)

Copy-paste the block below into Claude Code, from the repo root.

---

Merge the two machine-level config files — `mf-watch.yml` and
`mf-cockpit.yml` — into one file per machine: `magnaflow.yml`, with
a `watch:` and a `cockpit:` section. Read the config-loading code
of both tools first (`tools/mf-watch/`, `tools/mf-cockpit/`), plus
the config paragraphs in `docs/fase4-mf-watch/ontwerp-v0.1.md` and
`docs/fase5-cockpit/ontwerp-v0.1.md`.

Why: MagnaFlow's config landscape has exactly two axes — per
project (`.magnaflow/config.yml`, travels in git, already shared by
worker and mf-run with their own sections) and per machine. The
machine axis should be one file too. What must NOT change: project
configs stay per repo (self-contained, git-native), and
machine-specific values (`git_sync`, `notify_command`, the
cockpit's `projects:` paths) stay out of git — this merge
consolidates files, it does not centralize project state.

Target state:

- **One `magnaflow.yml` per machine**: all current mf-watch.yml
  fields under `watch:`, all current mf-cockpit.yml fields under
  `cockpit:`. Each tool reads only its own section and ignores the
  rest (the `IgnoreUnmatchedProperties` posture both already have)
  — future tools claim their own section without touching existing
  ones. Every field keeps its default; no file at all keeps
  working, as today.
- **Lookup order, per tool**: (1) `--config <path>`; (2)
  `magnaflow.yml` next to the binary; (3) the user config dir —
  `%APPDATA%\MagnaFlow\magnaflow.yml` on Windows,
  `~/.config/magnaflow/magnaflow.yml` elsewhere; (4) legacy: the
  tool's old file name next to its binary (`mf-watch.yml` /
  `mf-cockpit.yml`), read exactly as before but with a one-line
  deprecation notice on console/log. First hit wins.
- **Old-format tolerance**: a file (from `--config` or any lookup
  step) that contains the tool's section is read as sectioned; a
  file without it but with recognizable top-level legacy fields is
  read as legacy, with the same deprecation notice. Nothing ever
  errors merely for being in the old shape.
- **Unchanged**: `.magnaflow/config.yml` and `docs/specs/config.yml`
  (project axis); mf-run and the worker controller have no machine
  config and gain none.

Requirements:

- Each tool keeps its own config loader — no shared library, per
  the standing decoupling rule.
- Unit tests per tool, existing style: sectioned file read; the
  other tool's section ignored; legacy file fallback + notice;
  old-format file via `--config` + notice; full lookup-order
  precedence (a fake filesystem or temp dirs); missing file =
  all defaults.
- End-to-end sanity: run each compiled binary once against a
  sectioned `magnaflow.yml` and once against its legacy file, and
  confirm identical effective behavior (mf-watch `--once` against
  a disposable project suffices; cockpit: starts and serves
  `/api/projects`).
- Update the docs that name the old files: the config paragraphs
  in `docs/fase4-mf-watch/ontwerp-v0.1.md` and
  `docs/fase5-cockpit/ontwerp-v0.1.md` get an "**Updated:**"
  note (house style — see how fase-1 docs point at superseded
  structures) pointing at `magnaflow.yml` and this fase.
- Both tools' full suites green; zero behavior change for a
  machine still running the old files — the legacy path is the
  proof, not an afterthought.
- If anything about the lookup order or legacy detection turns
  out ambiguous against the real code, stop and ask rather than
  guess.

When done, write completion notes to
`docs/fase7-machine-config/completion-notes.md` in the usual
style: what shipped, validation performed, deviations, left for
later.
