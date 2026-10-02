# Build prompt — mf-cockpit v0.4 (Add project)

Copy-paste the block below into Claude Code, from the repo root.

---

Extend `tools/mf-cockpit/` per the design in
`docs/fase5-cockpit/ontwerp-v0.4.md`. Read that file first; it is
the spec. Also read `docs/fase5-cockpit/ontwerp-v0.1.md` (the
invariant and the write-action list — this fase adds write #6),
`docs/fase7-machine-config/completion-notes.md` (the config
loader you are extending with a resolved-path return, and the
lookup order the "Writing magnaflow.yml" cases follow),
`docs/mf-spec/README.md` + `docs/mf-spec/system.md` (the spec-kit
adoption layout the scaffold reproduces, and the `.gitignore`
runtime-ignore set), and the existing draft-creation code path
(write #1) which the seeded first command reuses. This fase
touches only the cockpit.

Summary of what you're building:

1. **Config**: `cockpit: new_project: {root, spec_kit,
   templates}` in the machine config — templates are
   `{name, type: copy, source}` or `{name, type: command,
   command, args, timeout_seconds}` with `{target}`/`{name}`
   placeholder substitution in args. Same
   `IgnoreUnmatchedProperties` posture as the rest.
2. **`CockpitConfig.Load` exposes its resolved path** so the
   append knows which file it loaded (sectioned file → edit it;
   legacy `mf-cockpit.yml` → refuse; no file → create
   `magnaflow.yml` in the user config dir).
3. **`GET /api/new-project`**: `{root?, templates: [names],
   specKit: bool}` — names only, definitions never leave the
   server.
4. **`POST /api/projects`** (write #6), `mode: "new"`: validate
   (unique name, sanitized dirname incl. Windows reserved-name
   check, target must not exist, template must exist) → mkdir →
   template (copy skips top-level `.git`; command spawns with
   placeholders, cwd = target, hard timeout + tree-kill, exit
   code ≠ 0 fails) → spec kit copy into `docs/spec-kit/` →
   hygiene files where the template didn't provide them
   (`.gitignore` runtime-ignore lines, minimal commented
   `.magnaflow/config.yml` stub) → `git init` (skip if `.git`
   exists) + `git add -A` + commit `cockpit: create project
   <name>` → seed the adopt draft via the existing write-#1 path
   (content pointing at `docs/spec-kit/0001-adopt-spec-system.md`;
   a draft, never ready) → register (config append + in-memory
   registry) **last** → SSE kind `projects`. Scaffold failures
   return captured output and leave the directory in place,
   unregistered. `mode: "existing"`: path exists and is a dir,
   duplicate name/path → 409, missing `.git` → warning not
   rejection; then register + SSE only.
5. **The config append is a text edit** (never
   parse-and-regenerate — comments and ordering survive), the
   four shapes from the design: existing block list, inline
   `projects: []`, `cockpit:` without the key, no `cockpit:`
   section. Write `magnaflow.yml.bak` first, re-parse and verify
   the new entry round-trips, restore the `.bak` and 500 on any
   failure. One server-side lock across scaffold + register.
6. **Frontend**: a `+` on `index.html` (header and empty state)
   opening a modal with the two modes; New shows the template
   dropdown and a live target-path preview, and is disabled with
   an explanatory hint when `new_project.root` is missing; on
   success navigate to `project.html?p=<name>`; open index pages
   refresh their list on the `projects` SSE kind.

Requirements beyond the design doc:

- Unit tests in the existing style: dirname sanitization (strip,
  spaces, collapse, empty result, reserved names), all four
  text-append shapes each preserving surrounding comments
  byte-for-byte, placeholder substitution, validation order, the
  legacy-file refusal, and the no-file → user-config-dir case
  (via the fase-7 `LocatePath` temp-dir overrides).
- `WebApplicationFactory` integration tests against temp dirs: a
  full `mode: "new"` create with a `copy` template (scaffold on
  disk, initial commit present with the right message, seeded
  draft in the lane as `draft`, entry appended to a temp
  `magnaflow.yml`, `.bak` written), a `command` template via a
  stub executable (same swap mechanism as `chat.command`/
  `run.command` stubs) incl. a failing stub (dir left in place,
  nothing registered) and a hanging stub (killed by timeout),
  `mode: "existing"` incl. duplicate-name and duplicate-path 409s
  and the missing-`.git` warning, target-already-exists rejection,
  and a corrupted-append simulation proving the `.bak` restore.
- Live browser verification (`/browse`) as in v0.1–v0.3: create a
  real project from the dialog with a template, watch index
  refresh via SSE, land on the project page, see the seeded
  draft; register an existing working copy; try a duplicate name
  (see the 409 rendered); console clean throughout.
- No behavior change for every existing page and endpoint — run
  the full existing cockpit suite.
- If the spec-kit adoption flow, the worker's expectations of
  `.magnaflow/config.yml`, or the fase-7 loader's shape
  contradict what this prompt assumes, stop and ask rather than
  guess.

When done, write completion notes to
`docs/fase5-cockpit/v0.4-completion-notes.md` in the same style as
`v0.3-completion-notes.md`: what shipped, validation performed,
deviations, left for later. Also update
`docs/fase5-cockpit/ontwerp-v0.1.md`'s write-action list
(write #6).
