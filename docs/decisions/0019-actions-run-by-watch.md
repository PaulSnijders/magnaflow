---
date: 2026-10-13
topic: watch, worker, cockpit, actions
status: accepted
---

# 0019 — Project actions: scheduled by mf-watch, run by mf-worker

## Situation

Projects want named jobs of their own: sync a kanban board into the lane,
update dependencies at night, resolve a rebase conflict that git_sync
gave up on. They should start from a button or on a schedule, and have a
visible log. Some write cmds that the worker then executes, possibly as
automatic bug fixes. The worker and these jobs share one persistent
working copy, so overlap is not just untidy: it is a correctness problem
(dirty tree, locked ports, rebase conflicts). Decision 0006 kept mf-watch
to polling and dispatching. The brainstorm is in `.scratch/` and is not
part of the record; its premises are restated here.

## Options

1. **No new mechanism**: a project script on an OS timer (systemd, Task
   Scheduler) writes cmds and commits, and git_sync picks them up. There
   is no button and no log in the cockpit, and the timer does not know
   when a worker run is busy.
2. **mf-watch as scheduler, mf-worker as executor**: definitions in a
   committed `.magnaflow/actions.yml`. mf-watch runs due and requested
   actions inside its single-threaded poll by spawning
   `mf-worker action <name>`. The cockpit shows them and writes a
   request file.
3. **The same, but mf-watch executes the actions itself**, including
   `claude -p`.
4. **A workflow engine**: steps, DAGs, its own runner, agent steps. This
   would duplicate the worker. GitHub/GitLab-style CI assumes an
   ephemeral checkout per run and a forge that sends events, and
   MagnaFlow has neither.

## Measurement

- **Serialization for free.** mf-watch's loop already runs one thing at
  a time. An action between dispatches can never overlap a worker run,
  so there is no lock and no busy flag. A request that arrives during a
  run waits for the next poll.
- **By hand.** 0006 requires that everything mf-watch automates stays
  doable by hand. `mf-worker action <name>` is that path. Option 3 has
  none, short of copying the command line.
- **Reuse.** The worker already has `IAgentRunner`, the agent config,
  timeouts and the dirty-tree guard. Option 3 would give mf-watch a
  second agent runner and make it read `config.yml`, which it never
  does today.
- **Agent code work stays in the lane.** An action writes cmds rather
  than letting the agent edit code outside a run, so specs-first, the
  rst, retries and `branch:` for automatic fixes keep working.
- **Opt-in.** A separate file, not a section in `config.yml`, keeps
  actions an add-on that a project switches on by creating it. The
  cost is one `.gitignore` line per adopting project, because
  `.magnaflow/*` is ignored.

## Choice

Option 2. It widens 0006's dispatcher with one job: deciding *when* a
project action runs. mf-watch still executes nothing itself. It spawns
the worker and reads exit codes. Actions run only while the project's
watcher runs. Without `actions.yml`, nothing changes.

Current behavior: `specs/concepts/actions.md`, `specs/worker/action.md`,
`specs/watch/mf-watch.md#one-poll`, `specs/cockpit/project.md#actions`.
