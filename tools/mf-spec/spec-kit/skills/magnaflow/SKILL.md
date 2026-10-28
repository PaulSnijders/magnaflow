---
name: magnaflow
description: "How this project is run by MagnaFlow (mf-worker, mf-watch, mf-run, mf-cockpit): the `.magnaflow/` files, `config.yml` (build, test, agent, run services, stable instance) and `actions.yml` (named jobs started from the cockpit or on a schedule). Use when creating or changing a MagnaFlow action, editing `.magnaflow/config.yml`, or when the user asks what MagnaFlow can do or why the worker, watcher or an action behaves as it does."
---

# MagnaFlow in this project

MagnaFlow is a set of small tools that run this repo's prompt lane
(`docs/prompts/`) on a machine:

- **mf-watch** polls the lane. With `git_sync` on it pulls first and
  pushes after. It hands each `ready` cmd to the worker, one at a time,
  then runs the project's due or requested actions.
- **mf-worker** executes one cmd: the agent plans and implements it, then
  the build and test commands from `config.yml` run, and the worker
  commits the result and sets the status. `mf-worker action <name>` runs
  one action.
- **mf-run** stops the project's own dev processes before a worker run
  and starts them again after it. Optionally it also keeps a published
  *stable instance* running that a worker run never stops.
- **mf-cockpit** is the window: the lane, the run evidence, and the Run,
  Watcher and Actions cards.

The tools need not be installed on the machine where you write cmds,
config or actions. They only need to be installed where the watcher runs.

## Files

| File | In git | Owned by |
|---|---|---|
| `.magnaflow/config.yml` | yes | the project: build, test, agent, run |
| `.magnaflow/actions.yml` | yes, optional | the project: named jobs |
| everything else in `.magnaflow/` | never | the tools, machine-local |

The machine-local rest is the worker's per-cmd logs and `session.yml`,
the watcher's log and lock, mf-run's `run/` and `stable/`, and the
actions' `.log`, `.last.yml` and `.request`. `.gitignore` carries:

```gitignore
.magnaflow/*
!.magnaflow/config.yml
!.magnaflow/actions.yml
```

Never commit or hand-edit the machine-local files. Read them to answer
"why".

## Standing rules

- **Leave the tree clean.** The worker and every action refuse to start
  on a dirty tree, and an action that leaves it dirty has failed.
  Untracked files count.
- **Code work goes through a cmd in the lane.** An action that wants code
  changed writes a cmd file. It does not edit the code itself.
- **Nothing installs, updates or restarts the MagnaFlow tools**: no cmd,
  action, build or test. The human rolls them out by hand.
- Project-specific notes go in the project's own `CLAUDE.md`. This skill
  is overwritten by every spec-kit update; where
  `docs/specs/PROJECT.md` has a `## magnaflow` section, it wins over
  this skill.

## Topics

| Read | When |
|---|---|
| [config.md](config.md) | editing `.magnaflow/config.yml`: build/test commands, agent, `run.services`, `run.stable` |
| [actions.md](actions.md) | creating, changing or debugging an action in `.magnaflow/actions.yml` |
