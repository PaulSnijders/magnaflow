---
title: "Spec kit 1.4: an optional magnaflow skill, installed when the project runs on MagnaFlow"
cmd: 0030-cmd-kit-magnaflow-skill.md
done: 2026-10-14
summary: "Spec kit is now 1.4: a yes to the MagnaFlow question installs a magnaflow skill (SKILL.md, config.md, actions.md) and the !.magnaflow/actions.yml ignore line, and this repo has it installed. To do: try it in a fresh session by asking for an hourly git fetch action."
---

# 0030 — kit-magnaflow-skill

## What was done

- **New kit skill** `tools/mf-spec/spec-kit/skills/magnaflow/`:
  - `SKILL.md`: the cmd's exact `description`, what each tool does with the repo, the files-ownership table with the three ignore lines, the standing rules, and a topic pointer table.
  - `config.md`: build/test, defaults, agent, `run.services`, `run.stable` and what the `publish` script must do.
  - `actions.md`: the six steps, the two examples from this repo (`git-status` with its `.sh`/`.cmd`, `resolve-conflicts`), the rules, and "why did it not run".
- **Adopt** (`0001`): the intro says 1.4. Step 5b now has three lines, plus a sentence on why `actions.yml` travels. New step 5c installs the skill. The closing summary says whether the MagnaFlow step ran and the skill was installed.
- **Update** (`0002`):
  - Step 1 says "every path to 1.4".
  - Step 6 on yes: ensure every step 5b line and overwrite `.claude/skills/magnaflow/`.
  - Step 6 on no: skip and leave an existing skill alone.
  - The pre-1.0 migration now says "the lines of step 5b" instead of "the two". A new "From 1.3 to 1.4" section says there is nothing to migrate beyond step 6.
- **Version and description**:
  - Stamp 1.4 in the kit README.
  - `KIT.md`: version line, "brought to 1.4", a `1.4:` paragraph, the rewritten neutrality paragraph, and a Contents row for `skills/magnaflow/`.
  - `tools/mf-spec/README.md`: the adopt bullet now has the third line and the skill.
  - `tools/mf-spec/system.md`: version 1.4, the skill in the neutrality sentence, and the runtime-plane bullet and layout comment now name `actions.yml` as committed. The runtime-plane bullet was stale since 0029.
- **Root `CLAUDE.md`**: `.claude/skills/magnaflow/` is added to the installed copies, plus a new bullet for the same-commit rule. The file is now 417 words.
- **Dogfood (0002 run by hand, MagnaFlow = yes)**:
  - Step 2: overwrite set copied. Only the README stamp actually changed (1.3 → 1.4).
  - Step 3: `CLAUDE-section.md` is unchanged, so the re-merge was a no-op.
  - Step 4: everything is present. `docs/prompts/` has no `.gitkeep`, but it holds files, so I added none. `.gitattributes` covers md/mjs/yml through `* text=auto eol=lf`.
  - Step 5: nothing to remove.
  - Step 6: `config.yml` was left untouched, and the three ignore lines were already in `.gitignore`. The skill was copied.
  - The final "delete the kit copy and commit" step was skipped on purpose. The master in `tools/mf-spec/spec-kit/` is not a stray copy, and the controller owns git.

## Verification

- `node scripts/spec_lint.mjs`: 0 problems.
- `diff -r` is empty for `skills/magnaflow`, `skills/specs`, the three commands, `spec_lint.mjs`, `docs/specs/README.md` and `docs/CLAUDE.md`.
- I read the skill files against `concepts/actions.md`, `worker/action.md`, `concepts/machine-config.md`, `run/mf-run.md`, `watch/mf-watch.md`, `concepts/evidence-layout.md`, `concepts/worker-run.md`, `cockpit/project.md` and decision 0010. I found no contradiction.
- Not done, left for the human: the fresh-session check ("maak een magnaflow-actie die elk uur `git fetch` doet").

## Spec points left out of the skill on purpose

- Machine config (`magnaflow.yml`), the watcher's own config (intervals, backoff, notify), lookup order: these are machine concerns, not a project author's.
- Action internals: the request/wake-file mechanics, request-deletion edge cases, refusal log de-duplication, the exact stdout lines, exit code 4.
- mf-run internals: PID files, the PID-reuse guard, stale reasons, `--json` status, the promote swap/rollback sequence, `promote.lock`, `state.yml` fields, exit codes. The skill keeps only "a failed publish leaves the previous instance running" and the publish log.
- Worker internals: plan phase, session resume, retry prompts, evidence commits, branch bookkeeping. These are lane matters, already covered by `/cmd-go` and the kit README.
- Git sync conflict handling. `resolve-conflicts` appears only as an example.
- `run.command` (how the worker finds mf-run) is shown in no example. It is rarely needed by a project author.

## Decisions taken while implementing

- The skill goes in only through 0001 step 5c and 0002 step 6, never through step 2. That keeps "no" projects byte-for-byte on the 1.3 behaviour, apart from the version stamp and text in the prompts themselves.
- The `description` is quoted in the YAML frontmatter, because it contains `:` and backticks.
- `config.md` gives the `dotnet run` reason from decision 0010 (it spawns a child and rebuilds on start). It also recommends an `agent.args` permission mode, because the worker runs the agent non-interactively. That is this repo's own experience (0019, see the comment in `config.yml`), not a spec rule. It is worded as advice.
- The `publish` requirement "write nothing into the working tree" is inferred from the clean-tree rule, not stated in `mf-run.md`. It follows directly: the next worker run would refuse.
- Word counts with `wc -w`, including YAML/code blocks and frontmatter: SKILL.md ~460 (body ~390), config.md and actions.md ~650–690 each. The prose is under ~600. The overshoot is the examples, which the cmd asked for.

## Self-answered questions

- *Should the skill also be placed by 0001 step 2 or 0002 step 2?* No, only in the MagnaFlow step. Otherwise "no" projects would change.
- *Was a behaviour spec in `docs/specs/` affected?* No. No tool behaviour changed, and `evidence-layout.md` and `actions.md` already describe `!.magnaflow/actions.yml`. Only `tools/mf-spec/system.md`'s runtime-plane line was stale, and I corrected it.
- *Should the dogfood run delete the kit copy or commit, as 0002's last step says?* No. The master is not a stray copy, and git belongs to the controller.
- *Should the dogfood add `docs/prompts/.gitkeep`?* No. The step's aim is that `docs/prompts/` exists and travels, and it holds tracked files.
- *Does the Contents table in KIT.md need a row?* Yes. I added one, marked kit-owned and only on yes.
- *Where do the cockpit card names and the ignored-file warning come from?* From `docs/specs/cockpit/project.md` (Actions card, Run card, Promote).
