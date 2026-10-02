# Docs audit — spec kit vs. fase 6/7/cockpit-v0.4, root README (2026-07-15)

Audit of `tools/mf-spec/spec-kit/` and the "as it is now" docs
(`docs/mf-spec/*`, `docs/kennis/*`) for drift against the last three
landed changes: fase 6 (mf-run), fase 7 (`magnaflow.yml`), cockpit v0.4
(Add project). Docs-only change, no behavior touched. Also adds the
repo's root `README.md`.

## Checked and fixed

- **`tools/mf-spec/spec-kit/0001-adopt-spec-system.md` step 3b
  (worker config) — not idempotent against the cockpit's scaffold.**
  The cockpit's "Add project" (v0.4) pre-creates `.magnaflow/config.yml`
  as an all-comments placeholder stub (`Config/ProjectScaffolder.cs`,
  `ConfigStub`). The old wording ("if it does not exist, create it...
  do not overwrite an existing file") would see that stub as "exists"
  and skip filling in real build/test commands, leaving the project
  with a worker config that's just comments. Fixed: now also fills in
  a config that exists only as a commented-out placeholder (no active
  `build:`/`test:` keys); still leaves a config with real values alone.
- **Step 3c (gitignore lines) — same class of gap.** The old wording
  ("Add `.magnaflow/mf-watch.log`, ... to `.gitignore`") assumed
  starting from nothing. The cockpit scaffold already adds all three
  lines (`ProjectScaffolder.EnsureHygieneFiles`), so a blind "add"
  risks duplicate lines. Fixed to explicit merge language ("add only
  whichever of the three lines are missing"), matching the phrasing
  `0002-update-spec-system.md` already used for the same lines.
- **Step 1 / step 4 — no path for a genuinely empty new project.** The
  cockpit can scaffold a project with no code template at all
  ("(empty)" option). The adopt prompt's "derive surfaces from the
  code" and "write initial specs from the code" steps had no explicit
  handling for "there is no code yet." Fixed: step 1 now allows leaving
  `surfaces: {}` and skipping step 4 when there's no code, while still
  doing the kit-file/CLAUDE/worker-config/gitignore steps; step 3b
  notes leaving the config stub alone (nothing to derive commands from)
  in that same case.
- **`docs/mf-spec/README.md` "Use it in a project" step 2 — missing
  gitignore line.** Listed only `.magnaflow/mf-watch.log` and
  `.magnaflow/mf-watch.lock`; `.magnaflow/run/` (added by fase 6, mf-run's
  PID files + service logs) was missing. `system.md` already listed all
  three. Fixed to match.
- **`docs/mf-spec/README.md` "Use it in a project" — cockpit route
  missing.** Only documented the manual-copy path. Added the cockpit
  "Add project" path (pre-copies the kit, pre-adds the gitignore lines,
  seeds the adopt prompt as a draft — mark it `ready`) as the second way
  in, per `docs/fase5-cockpit/v0.4-completion-notes.md`.
- **Root `README.md` — did not exist.** Written as a one-screen map:
  what MagnaFlow is, the five tools (one line each), the two config
  axes (`.magnaflow/config.yml` per project, `magnaflow.yml` per
  machine), the docs layout, and where to start. Points at
  `docs/mf-spec/README.md` and the fase completion notes rather than
  duplicating their content.

## Verified and left alone

- **Fase 6 gitignore lines in the kit.** All three lines
  (`.magnaflow/mf-watch.log`, `.magnaflow/mf-watch.lock`,
  `.magnaflow/run/`) were already present and correct everywhere in the
  kit that names them: `0001-adopt-spec-system.md` step 3c, KIT.md's
  0.8→0.9 migration note, `0002-update-spec-system.md`'s final
  gitignore instruction. The completion notes' claim that the kit was
  updated for this checks out — no drift found.
- **Fase 7 config filenames in the kit.** Grepped the whole
  `tools/mf-spec/spec-kit/` tree and `docs/mf-spec/*` for
  `mf-watch.yml` / `mf-cockpit.yml`: zero matches outside this audit's
  own build prompt (which quotes the finding itself, not a doc claim).
  Machine config is outside the kit's domain to begin with (it governs
  `docs/specs/` and `docs/prompts/`, not `magnaflow.yml`), so there was
  never a filename to go stale here.
- **`docs/spec-kit/` pre-existing when the cockpit scaffolds a
  project.** `0001-adopt-spec-system.md` already describes itself as
  living inside "the `docs/spec-kit/` folder next to this prompt" —
  that's exactly what the cockpit scaffold produces (kit copied into
  `docs/spec-kit/`, prompt seeded pointing at
  `docs/spec-kit/0001-adopt-spec-system.md`). No wording assumed a
  human pasted the folder in by hand; both paths converge on the same
  starting state, so no fix was needed for this specific sub-point.
- **`docs/mf-spec/system.md`.** Already carries all three gitignore
  lines and no stale filenames or superseded cockpit-capability claims;
  read in full, left unchanged.
- **`docs/kennis/cockpit.md`, `docs/kennis/spec-strategie.md`.**
  Background/idea notes, not "cockpit can/can't do X" claims about the
  current build — nothing in either references a config filename or a
  cockpit limitation that v0.3/v0.4 made untrue. Left unchanged.

## Flagged for a human decision

- None. Every suspected drift either checked out clean or had a
  concrete, mechanical fix within the docs/kit-only scope of this
  audit.
