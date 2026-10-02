# Quickstart: Validating Plan, Questions & Feedback (v0.2)

Proves the feature end-to-end: plan-and-self-answer, pause-and-resume on a genuine question, the
report, and convention inheritance. References [contracts/cli.md](contracts/cli.md) and
[contracts/file-formats.md](contracts/file-formats.md) instead of duplicating them.

## Prerequisites

- A target project (or `tools/worker-controller/examples/hello-website/`, migrated to
  `docs/prompts/`) with `.magnaflow/config.yml` (build/test commands) and, ideally, a `CLAUDE.md`
  and/or constitution file to exercise convention inheritance.
- `git`, and the configured agent CLI, installed and authenticated.
- `mf-worker` built (`dotnet build tools/worker-controller/`).

## Scenario A — fully-specified command completes without pausing (User Story 1)

1. Write `docs/prompts/0001-cmd-add-footer.md` with `status: ready`, a goal fully answerable from
   an existing linked spec (`specs:` pointing at a real file) and the project's own conventions.
2. Run `mf-worker run 0001-add-footer`.
3. **Expect**: `docs/prompts/0001-pln-add-footer.md` exists, its plan lists only self-answered
   questions (if any); no `0001-qa-add-footer.md` was created; the command's frontmatter status is
   `done`; `docs/prompts/0001-rst-add-footer.md` exists describing what was done; exit code 0.

## Scenario B — genuine question pauses the command, then resumes (User Story 2)

1. Write `docs/prompts/0002-cmd-add-banner.md` whose acceptance criteria depend on a product
   decision not present anywhere in specs/code/conventions (e.g., "pick a banner message" with no
   copy given anywhere).
2. Run `mf-worker run 0002-add-banner`.
3. **Expect**: run ends with frontmatter `status: questions`; `docs/prompts/0002-qa-add-banner.md`
   exists with the open question; no build/test command ran (check `.magnaflow/0002-add-banner/`
   has no `build.log`/`test.log` this attempt); `docs/prompts/0002-rst-add-banner.md` does **not**
   exist yet; exit code 0.
4. Edit `0002-qa-add-banner.md`, writing an answer beneath the question; set the cmd's
   `status:` back to `ready`.
5. Run `mf-worker run 0002-add-banner` again.
6. **Expect**: the agent log (`claude.log`) shows a resumed session, not a fresh
   self-introduction; `0002-pln-add-banner.md` is updated in place (same file, reflects the
   answer); the run proceeds to `done` (or pauses again only if a genuinely new question
   surfaces); `0002-rst-add-banner.md` now exists.
7. **Also verify** (User Story 2, scenario 3 in spec): attempting `mf-worker run 0002-add-banner`
   while its status is still `questions` (before step 4) is refused (exit 3) — there is no
   shortcut around the human's own status reset.

## Scenario C — report reflects an aborted outcome (User Story 3)

1. Write a command whose acceptance criteria cannot be met by the agent even after
   `max_attempts` retries (e.g., a build command that always fails for reasons unrelated to the
   agent's changes).
2. Run it to exhaustion.
3. **Expect**: frontmatter `status: aborted`; the rst file explicitly states the reason (retries
   exhausted) and reflects the final attempt, not the first; exit code 1.

## Scenario D — convention inheritance (User Story 4)

1. Ensure the target project has a `CLAUDE.md` at its root and/or a constitution file at
   `.specify/memory/constitution.md` (or `docs/constitution.md`).
2. Run any `ready` command whose own `specs:` list does **not** include either file.
3. **Expect**: inspecting `.magnaflow/<id>/claude.log`'s recorded prompt (or an equivalent
   inspectable input) shows the convention content present, even though it was never listed under
   `specs:`.
4. Repeat in a project with neither file present.
5. **Expect**: the run proceeds exactly as in v0.1, no error.

## Scenario E — manual-parity guarantee (cross-cutting principle)

1. Pause a command on `questions` via the controller (Scenario B, step 3).
2. Instead of running `mf-worker` again, a human pastes the cmd + pln + qa content into an
   interactive coding session by hand, gets the work done, and hand-writes `NNNN-rst-name.md`
   plus sets the cmd's `status:` to `done` directly.
3. **Expect**: a later `mf-worker status` shows this command as `done` with no complaint or
   special-casing — indistinguishable from a controller-completed run (FR-022/FR-023, SC-006).
