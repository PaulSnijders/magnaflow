---
title: "Opt-in stable instance: mf-run promote, worker promotes after done, cockpit stable row"
status: done
created: 2026-10-09
attempts: 1
---

## Context

The human checks a run's result through the project's debug services,
over Tailscale. The worker stops them for every implementation, and the
next command is often queued right away, so the app is down when the human
wants to look at it. Decision `docs/decisions/0018-stable-instance.md`
adds an opt-in **stable instance**: a published build in
`.magnaflow/stable/current/`, on its own port, that a run never stops. It
is replaced by `mf-run promote` (publish → swap → start), which the worker
runs after a branchless `done` and the cockpit offers as a button.

The specs are already ahead and are the work order. Read them first:

- `docs/specs/run/mf-run.md`: usage, config `run.stable`, section
  `#stable-instance`, exit code 3, integration.
- `docs/specs/concepts/worker-run.md`: step 8 and "mf-run around the run".
- `docs/specs/cockpit/project.md#run`: the stable row.

The pilot project is triferto-forecast. Its frontend is built into the
backend and served at `/app`.

## Task

1. **mf-run config** (`Config/RunConfig.cs`): parse the optional
   `run.stable` block (`publish`, `command` required; `args`, `url`,
   `link`, `timeout_minutes` optional, default 15). A service named
   `stable` is a validation error (exit 2). A missing block means no
   stable, and every existing config stays valid.
2. **mf-run verbs**:
   - `start|stop|restart|status stable` act on the stable process: PID
     and log as `.magnaflow/run/stable.{pid,log}`, workdir
     `.magnaflow/stable/current/`, `command` resolved inside that folder
     with the existing extensionless resolution. Reuse
     `ServiceManager`/`PidFile`; do not fork a second process manager.
   - The verbs without a service name never include stable.
   - `status` (text and `--json`) appends the stable entry with the
     additive fields from the spec.
3. **`mf-run promote`**, exactly the steps in
   `mf-run.md#stable-instance`:
   - an OS-level exclusive lock on `.magnaflow/stable/promote.lock`
     (follow mf-watch's instance lock: `FileShare.None` on Windows, an
     exclusive `flock` elsewhere). Held means exit 3;
   - `state.yml` writes: `building`, then `ready` or `failed` with a
     `message`. An interrupted `building` reads as `failed` in `status`;
   - publish with a timeout, with output to
     `.magnaflow/run/stable-publish.log`;
   - swap with a short rename retry. On a failed rename or a new process
     that dies within the start check, restore `prev/` → `current/` and
     start it.
   - git (`rev-parse HEAD`, `status --porcelain`) goes through the
     existing process seam, so tests can fake it.
4. **mf-worker** (`Execution/TaskRunner.cs`): after the terminal commit
   and push of a `done` run, with no `branch:` and with `run.stable`
   configured (read the presence the way the worker already reads
   `run.services`), spawn `<run.command> promote --project <root>`.
   Wait for it and print one stdout line with the exit code. It never
   changes the status, the rst or the worker's exit code. Skip it after
   `aborted` and `questions`.
5. **mf-cockpit**:
   - a stable row on the Run card as in `project.md#run`;
   - Promote endpoint: 409 while a command is `running` or stable's
     `state` is `building`. Otherwise spawn `mf-run promote` **detached**
     (do not wait under the cockpit's `run.timeout_seconds`) and return
     at once;
   - **Behind** compares stable's `sha` with the HEAD the cockpit
     already knows from its git info;
   - the link is `link` verbatim, else `url` through the existing
     `displayUrl` rewrite.
6. **Tests** (xUnit, with fakes for the existing seams):
   - mf-run config: `stable` parsed; defaults; service name `stable`
     rejected; old configs unchanged.
   - promote: happy path (state `building` → `ready`, folders swapped,
     `prev/` kept); publish fails (exit 1, `failed`, `current/`
     untouched, old process not stopped); publish timeout; new process
     dies (prev restored and started); lock held (exit 3, nothing
     changed); stale `building` without a lock reads as `failed`.
   - `stop` and `start` without a name leave stable alone.
   - worker: promote spawned after a branchless `done`; not after
     `aborted`, `questions`, or a `branch:` done; not without
     `run.stable`; a failing promote leaves exit code and status
     unchanged.
   - cockpit: Promote 409 while running or building; spawn is detached.
7. **Spec-first**: the three specs are already written. Adjust them only
   where the implementation must deviate, and say why in the rst.
   Update `docs/specs/concepts/evidence-layout.md` for the new
   machine-local paths (`.magnaflow/stable/`,
   `.magnaflow/run/stable*.{pid,log}`, `stable-publish.log`).

## Must not change

- Behavior for projects without `run.stable`: byte-for-byte the same
  worker run, mf-run output and cockpit card.
- The debug services' stop before implementation and start after the
  terminal status.
- mf-run's existing exit codes 0/1/2 and `status --json` fields (new
  fields are additive only).

## Verify

- `dotnet test` for mf-run, worker-controller and mf-cockpit.
- After the run, by hand (needs `tools/install/install.ps1`; the worker
  never installs): in triferto-forecast add an `mf-publish-stable`
  wrapper (dotnet publish, the frontend build into the publish output,
  and the project's existing `.sh` script that builds the Help files)
  and a `run.stable` block on a free port. Point `tailscale serve` at that
  port. Then:
  - `mf-run promote` from the cockpit: the row goes building → ready,
    `/app/` works via the link, and the Help pages load. This verifies the
    content root is the published dir;
  - run a small cmd: the stable app stays reachable through
    implementation and is replaced a few seconds after `done`;
  - break the publish script once: the row shows failed, and the old
    stable keeps serving.

## Not in scope

- Anything in `/cmd-go`: a manual run ignores the stable instance. It
  belongs only to the worker workflow; the cockpit's Promote button
  covers the rare manual case.
- Publishing in the background from a separate worktree, so the next run
  does not wait. Only if the wait turns out to hurt (decision 0018).
- A rollback button. `prev/` is kept, and a rollback is a manual rename
  for now.
