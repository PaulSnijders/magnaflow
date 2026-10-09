---
date: 2026-10-09
topic: run, worker, cockpit
status: accepted
---

# 0018 — An opt-in stable instance, promoted by mf-run after `done`

## Situation

The human checks a run's result through the project's debug services,
over Tailscale. The worker stops those services right before
implementation (a running exe locks `bin/`, decision 0010) and starts
them after the terminal status. The human often queues the next command
right away, so the app they want to look at is down for the whole
implement, build and test of the next run. Decision 0010 put "anything
deploy-shaped" out of scope for mf-run.

## Options

1. **Accept the downtime.** Free, and the problem stays.
2. **Keep the debug services running**: run them from a snapshot of the
   build output, skip the stop, restart at the end. The content root is
   still the source tree the agent edits, `ng serve` live-reloads
   half-finished edits, and the agent may want the same port.
3. **A separate stable instance**: a published build in its own
   machine-local folder and port, never stopped by a run, replaced only
   by an explicit promote step that publishes, swaps and restarts.

## Measurement

- The projects compile their frontend into the backend, so a published
  build is one process on one port.
- Option 2 fights how a debug run works; option 3 separates "what the
  worker is changing" from "what the human looks at".
- Promotion inside the worker run, after the terminal commit, cannot read
  a tree that the next run is editing. It delays the next dispatch by
  the publish time (minutes), which is small next to implement plus test.
- A background publish from a separate worktree would avoid that wait,
  but adds a long-running process to supervise. Not worth it yet.

## Choice

Option 3, opt-in per project through `run.stable:`. Without that block
nothing changes. mf-run owns it (`mf-run promote`, plus the existing
verbs by the name `stable`). The worker spawns it only after a branchless
`done`, and reads only the exit code. The cockpit shows what stable runs
and can promote by hand.

This widens decision 0010's "nothing deploy-shaped" for one case only. It
is a local, machine-only copy of the project's own build, with no remote
target, no environments and no rollout.

Current behavior: `specs/run/mf-run.md#stable-instance`,
`specs/concepts/worker-run.md#mf-run-around-the-run`,
`specs/cockpit/project.md#run`.
