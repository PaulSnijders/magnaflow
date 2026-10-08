---
date: 2026-07-14
topic: run
status: accepted
---

# 0010 — mf-run v0.1 — design

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

Between "the worker finished" and "you are testing the result" there
was nothing. Claude Code tripped over locked ports and files while the
project's own app was running on the worker machine, and after a run
the human wanted the fresh build already up, to click through it
remotely. The core of the problem is process management: PID files,
killing a whole process tree (the reason ports stay locked), starting
several services in order, reporting status.

## Options

1. **A separate small tool, `mf-run`**, that the worker spawns around
   implementation and a human or the cockpit can call too.
2. **Process management inside the worker controller.**
3. **Leave it to the human** (stop the app by hand before a run, start
   it after).

## Measurement

- A bounded job with its own lifetime is a small deterministic tool in
  the MagnaFlow sense; the worker should stay thin.
- The manual lever matters: `mf-run stop` beats hunting a process tree.
- The cockpit later needs status and start/stop as plain shell-outs,
  not duplicated process logic.
- No library coupling between tools; mf-watch stays untouched.

## Choice

Option 1. Current behavior: `specs/run/mf-run.md`; its place in a run:
`specs/concepts/worker-run.md` ("mf-run around the run").

- **Config is per project**, in `.magnaflow/config.yml`: running is a
  property of the project, not the machine. A list of services from
  day one, because api plus frontend projects are real and a list is
  no harder than one item.
- **`command` is the built executable, not `dotnet run`**: `dotnet run`
  spawns a child and rebuilds on start; the built exe is what the
  worker just produced, starts instantly and dies cleanly.
- **Stop kills the whole tree** and is idempotent: an orphaned child is
  exactly the locked-port problem, and the worker calls stop
  unconditionally.
- **"The process is alive" is the start contract**, no health checks:
  whether it serves correctly is what the human is about to test.
- **mf-run does not manage binding or access**: reaching the instance
  from the human's machine is the service's own args; the VPN/LAN is
  the perimeter, as with the cockpit.

### Worker integration

- **Stop after the plan gate, right before implementation**, not at the
  start of the run: planning touches no code, so the app keeps running
  through planning and a `questions` pause, while the human answers.
- **A failed stop refuses the implementation** in the dirty-tree
  posture (command stays `ready`, no attempt consumed): the ports the
  stop should have released are still locked.
- **Start after `done` and `aborted` both**: after an abort the last
  build may still run, and the running app is often how you diagnose
  what went wrong. A failed start is a warning in the rst, never a
  retry or a status change.
- **mf-watch is deliberately untouched**: a stop refusal has the same
  shape as the dirty-tree refusal it already reports. No new statuses,
  no new exit codes.

`.magnaflow/run/` must be gitignored, otherwise it dirties the tree the
worker guards; the spec kit's adopt and update prompts carry the
ignore line.

The cockpit's Run card was designed here but built later
(decision 0008), together with `status --json`.

Not in scope for v0.1: health checks, log rotation, auto-restart on
file change, per-service environment variables, anything deploy-shaped,
auth or tunnels.
