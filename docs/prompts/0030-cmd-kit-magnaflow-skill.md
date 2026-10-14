---
title: "Spec kit 1.4: an optional magnaflow skill, installed when the project runs on MagnaFlow"
status: done
created: 2026-10-14
attempts: 1
---

## Context

A Claude session in a project that runs on MagnaFlow knows almost
nothing about MagnaFlow. The spec kit is tool-neutral. Its only
MagnaFlow part is one optional question in adopt (step 5) and update
(step 6), which fills `.magnaflow/config.yml` and the ignore lines. So
"maak een magnaflow-actie die elke nacht …" makes Claude guess: the file
name, the gitignore exception, scripts for both OSes, the clean-tree
rule, `script` versus `prompt`.

Decided with the product owner:

- **One generic skill, `magnaflow`**, not one per feature. Claude loads it
  on its own from the `description` when the topic comes up; `/magnaflow`
  works too. A short `SKILL.md`, plus one file per topic that is read
  only when needed.
- **Per project, through the kit's existing MagnaFlow question**, not per
  machine. Commands and actions are written on the dev machine, which
  may have no MagnaFlow install. Answer yes: the skill is installed.
  Answer no: nothing changes, and the kit stays neutral for projects
  without MagnaFlow.
- **The skill is kit-owned**: every update overwrites it, like
  `skills/specs/`. Project-specific notes belong in the project's own
  `CLAUDE.md`, not in this skill.
- **One source**: the master lives in the kit. This repo gets a rule that
  a behaviour change to `config.yml`, `actions.yml` or the run/stable
  config updates the skill in the same commit.

The truth about each feature stays in this repo's specs. The skill is the
how-to for a project author, written from them:
`docs/specs/concepts/actions.md`, `docs/specs/concepts/machine-config.md`
(project section), `docs/specs/run/mf-run.md` (config, stable instance),
`docs/specs/watch/mf-watch.md` (what the watcher does with a repo),
`docs/specs/concepts/evidence-layout.md` (what is machine-local).

## Task

1. **New in the kit: `tools/mf-spec/spec-kit/skills/magnaflow/`**
   - `SKILL.md`, at most ~400 words. Frontmatter `name: magnaflow` and
     this `description` (keep its trigger words):

     > How this project is run by MagnaFlow (mf-worker, mf-watch, mf-run,
     > mf-cockpit): the `.magnaflow/` files, `config.yml` (build, test,
     > agent, run services, stable instance) and `actions.yml` (named
     > jobs started from the cockpit or on a schedule). Use when creating
     > or changing a MagnaFlow action, editing `.magnaflow/config.yml`, or
     > when the user asks what MagnaFlow can do or why the worker,
     > watcher or an action behaves as it does.

     Body:
     - what MagnaFlow does with this repo, in a few lines: the watcher
       polls the lane, the worker runs `ready` cmds, mf-run starts and
       stops the dev processes, and the cockpit is the window;
     - the files a project owns (`.magnaflow/config.yml` and
       `.magnaflow/actions.yml`, committed) and the rest of `.magnaflow/`,
       which is machine-local and never committed;
     - the standing rules: leave the tree clean, code work goes through a
       cmd in the lane, nothing installs or restarts the MagnaFlow tools;
     - which topic file to read for what.
   - `config.md`: `.magnaflow/config.yml` for an author: `build`/`test`
     (`command` or `commands`, only commands that pass today), `defaults`,
     `agent`, `run.services` (the built exe, not `dotnet run`; resolution
     of extensionless commands) and `run.stable` (what it is, what the
     `publish` script must do). Say what each one changes in practice.
   - `actions.md`: how to make an action, step by step:
     1. pick `script` or `prompt`, and a schedule (`every_minutes`, `at`,
        or none for the button only);
     2. write the block in `actions.yml`;
     3. for a script: a `.cmd` and a `.sh`, called without an extension,
        with the `.sh` committed executable
        (`git update-index --chmod=+x`);
     4. make sure `!.magnaflow/actions.yml` is in `.gitignore`;
     5. commit;
     6. test once with `mf-worker action <name>`. It refuses on a dirty
        tree, so commit first.

     Also cover the rules: a guard refuses while a cmd runs or the tree is
     dirty; a tree left dirty counts as failed; the action may commit and
     the watcher pushes; code changes go into a cmd file the action
     writes (`status: ready`, optionally `branch:`). Include one example
     of each kind, using the two in this repo's `.magnaflow/actions.yml`.
     Point to the cockpit's Actions card and to the log and `last.yml`
     for "why did it not run".

   Each topic file stays under ~600 words. Describe what a project author
   needs. Do not retell the tools' internals, and do not repeat what the
   kit's `docs/specs/README.md` or `/cmd-go` already say about the lane.

2. **Adopt (`0001-adopt-spec-system.md`)**:
   - step 5b: a third ignore line, `!.magnaflow/actions.yml`, with one
     sentence on why (optional action definitions travel in git);
   - new step 5c: copy `spec-kit/skills/magnaflow/` to
     `.claude/skills/magnaflow/`;
   - the summary says whether the skill was installed.
3. **Update (`0002-update-spec-system.md`)**:
   - step 6 on yes: also ensure the step 5b lines, including the new
     one, and **overwrite** `.claude/skills/magnaflow/` from the kit;
   - on no: leave an existing `.claude/skills/magnaflow/` alone (do not
     delete it);
   - step 1: "every path to 1.4". Add a short "From 1.3 to 1.4"
     section if the file has per-version notes; there is nothing to
     migrate apart from step 6.
4. **Version and description**:
   - stamp `1.4` in `spec-kit/docs/specs/README.md`;
   - `KIT.md`: version line, a `1.4:` paragraph (why per project and not
     per machine, why one skill), and the neutrality sentence: the spec
     system stays tool-neutral, and the MagnaFlow skill and lines come
     only with a yes to the MagnaFlow question;
   - the matching lines in `tools/mf-spec/README.md` and
     `tools/mf-spec/system.md` (the version mention there says 1.3).
5. **This repo's rule** (root `CLAUDE.md`, "Dogfooding notes"):
   - add `.claude/skills/magnaflow/` to the list of installed copies;
   - one new bullet: a change to the behaviour of `.magnaflow/config.yml`,
     `actions.yml` or the run/stable config also updates
     `tools/mf-spec/spec-kit/skills/magnaflow/` in the same commit;
   - keep it short.
6. **Dogfood**: run `tools/mf-spec/spec-kit/0002-update-spec-system.md`
   on this repo, answering the MagnaFlow question with yes. Afterwards
   `.claude/skills/magnaflow/` must match the master, and so must the
   other installed copies (stamp 1.4 in `docs/specs/README.md`).

## Must not change

- Projects that answer "no" to the MagnaFlow question: byte-for-byte
  the same install and update as kit 1.3.
- The tools' code. This cmd changes kit files, the root `CLAUDE.md` and
  the installed copies only.
- The skills that become the project's own after install (`architect`,
  `brainstorm`, `cc-review`): untouched.

## Verify

- `node scripts/spec_lint.mjs` is clean.
- `diff -r tools/mf-spec/spec-kit/skills/magnaflow .claude/skills/magnaflow`
  shows nothing, and the same holds for the other installed copies listed
  in `CLAUDE.md`.
- Read `SKILL.md`, `config.md` and `actions.md` against the specs named
  in Context: no claim that contradicts them. Note in the rst any spec
  point left out on purpose.
- By hand, after the run, for the human: in a fresh Claude Code session
  in this repo, ask "maak een magnaflow-actie die elk uur `git fetch`
  doet". Claude reads the skill on its own and proposes a block in
  `actions.yml`, scripts for both OSes and the test command, without
  further explanation.

## Not in scope

- A per-machine install through `install.ps1`/`install.sh`: rejected,
  because the dev machine where cmds and actions are written may have no
  MagnaFlow install.
- Topic files for the lane, cmds or specs: the kit already covers them
  (`docs/specs/README.md`, `/cmd-go`).
- Rolling the new kit out to other projects: that is each project's own
  `0002` run, when the human chooses.
