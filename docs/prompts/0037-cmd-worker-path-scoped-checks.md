---
title: "Worker: path-scoped build and test commands (run a check only when the cmd touched its paths)"
status: done
created: 2026-11-27
attempts: 1
---

## Why

A monorepo (Klaas: `klaas-backend/`, `klaas-ionic/`, `topo/`) has one `build:` / `test:` list
for the whole repo. A cmd that only touches `topo/` still runs the full backend build, the
backend test suite and the Ionic build after **every attempt**. That costs time, and a red
check in an untouched part blocks the cmd.

The opposite problem follows from the same cause. Klaas leaves the topo checks out of
`config.yml`, because they would then also run for every backend cmd. So the worker never
checks topo work.

Projects need to say "this check belongs to these paths".

## Decisions (do not re-decide)

1. **A command entry may be a string (as today) or a mapping**:
   ```yaml
   build:
     commands:
       - node more/cmd/mf-klaas-backend.js build          # no paths: always runs
       - run: npm --prefix topo run build
         paths: [topo/]
   test:
     commands:
       - run: node more/cmd/mf-klaas-backend.js test
         paths: [klaas-backend/, more/cmd/mf-klaas-backend.js]
   ```
   - `run` is required.
   - `paths` is a non-empty list of repo-relative prefixes; a trailing `/` means a folder.
     There are no globs. Keep it simple.
   - The single-string `command:` form stays a string; `paths` exists only in `commands:`.
   - Invalid entries make the config invalid: a missing `run`, an empty `paths`, or an absolute
     path or `..`. That is exit 2, with a clear problem line, as other config errors.
2. **Touched files**: every file that differs between the cmd's start point and the working
   tree at the moment of the check. That includes:
   - commits made during the run;
   - uncommitted changes;
   - untracked files that are not ignored.

   The start point is the commit the worker made when it set the cmd `running`, or the HEAD it
   started from: whatever the worker already knows. A resumed run uses its own start point
   (`session-resume`), the earlier attempts' changes included.
   - Renames count as both paths.
   - Lane files (the cmd's own prompt files) do not count.
3. **When a path-scoped command runs**:
   - It runs when at least one touched file starts with one of its `paths`.
   - Otherwise it is skipped, and the attempt log says so:
     `attempt i/n: build 2/3 skipped (no change under topo/)`.
   - A skipped command counts as passed.
   - Commands without `paths` always run, as today.
   - Safety: if `.magnaflow/config.yml` itself was touched, every command runs.
4. **The test gate**: when every test command is skipped, the attempt passes as today's "no
   test gate" case, but with its own log line (`tests: all skipped, no touched paths`). The
   agent prompt's statement on who owns the tests stays true. The agent prompt lists, per
   check, whether it will run for the current changes, so the agent knows which checks will
   judge its work.
5. **Evidence**: `build.log` / `test.log` show the skipped commands with the reason, and the
   touched-path summary (the count per top-level folder) once per attempt. The cockpit needs
   no change beyond what it already shows from the logs.
6. **Other tools**:
   - Actions keep their own commands; `paths` is for `build:` / `test:` only.
   - mf-run and mf-watch are unchanged.
   - The cockpit's config view, if it parses `commands`, must accept the mapping form.

## Work

1. `ProjectConfig`: parse both entry forms. Keep the string form working unchanged (existing
   configs must parse to the same result). Validation and problem lines.
2. Touched-path computation in `GitClient` (or a small helper), behind the existing git
   abstraction. Unit tests against a scratch repo:
   - committed + uncommitted + untracked;
   - rename;
   - ignored files excluded;
   - lane files excluded;
   - a resume.
3. `TaskRunner`: filter per attempt, log lines, test gate (decision 4). `PromptBuilder`: the
   per-check list. Tests for:
   - a skipped command;
   - a mixed list;
   - all test commands skipped;
   - a touched `config.yml` running everything.
4. Specs: `docs/specs/worker/run.md`, `docs/specs/concepts/machine-config.md` (or wherever
   the `build`/`test` shape is specified), and `worker-run.md` for the attempt steps.
5. Docs: `tools/mf-spec/spec-kit/skills/magnaflow/config.md` (the new form, an example, "only
   commands that pass today" still applies per path), mirrored into this repo's installed
   copy, and `spec-kit.zip` rebuilt (see `CLAUDE.md`). Also `tools/worker-controller/README.md`
   / `EXAMPLES.md` where they show `commands:`.

## Report (rst)

Report:
- the final config shape with one example;
- how the start point is determined, including on resume;
- an example attempt log with a skipped command;
- anything a project must know before using `paths`. The tools roll out by hand or by
  self-update, so a project config with the mapping form breaks an older worker: say this
  plainly.
