# Working on a branch, and keeping the app up during a run
Status: direction chosen (A: habit; B: opt-in stable instance)
Supersedes: —

Two topics from one session; may split into two notes later.

## Problem

A. "Just work on a branch for a while" with a project. Today's workaround
idea: add the project twice (second clone), each on its own branch, toggle
the watchers. Costs: set up twice, second debug app needs other ports
(Triferto: HTTPS via Tailscale only fits one), and cmd numbering collides
between the two lanes.

B. The debug app is down while the worker implements (mf-run stop before
implementation, start after the terminal status). User would like the old
version to keep running until the run is done.

## What exists today (grounding)

- `branch:` / `base:` per cmd already exists (worker-run.md#branches):
  bookkeeping stays on the invoking branch (main), code goes to the work
  branch, pushed, human merges. The lane lives on main → no numbering
  collision. Cockpit copies `branch` into follow-ups; cockpit has a
  branch switch.
- mf-run stops right before implementation because of locked ports and
  locked files (decision 0010). `command` is the built exe in bin/.
- Build output is gitignored, so after a branch run the worker checks out
  main again but the running exe is the branch build.

- Verified in code: TaskRunner checks out `cmd.Branch`, then checks out
  the invoking branch again (TaskRunner.cs ~307/393). The cockpit shows the
  lane of whatever branch the checkout is on; a work branch shows a frozen
  snapshot — likely why it looked like "everything on one branch".
- Cmd id is number + slug (PromptScanner IdPattern). `0042-cmd-foo` and
  `0042-cmd-bar` coexist without a technical collision; only the human
  "cmd 0042" handle becomes ambiguous.

## User's answer on A

Two genuinely separate streams right now; rare. Proposal: MagnaFlow (watcher)
keeps running one branch; a second local checkout on another feature branch,
driven by hand with /cmd-go; at merge time a small cmd renumbers the side
branch's cmds.

## B grounding (triferto-forecast)

- Two services: `api` = built exe via wrapper, workdir = project dir
  (content root: wwwroot/, Help ContentPath read from source tree),
  https://localhost:7181; `app` = `ng serve` on 4204 (dev server, live
  reload). Tailscale host fronts these (review target "dev server 1").
- What actually forces the stop: the running api exe locks bin/ →
  `dotnet build` fails. ng serve locks nothing but would live-reload
  half-finished edits. Content root = source dir, so even a copied exe
  would read mid-edit wwwroot/help files.

Possibilities seen:
1. Accept the limitation (downtime = implement + build + test, up to ~30 min+).
2. Keep debug running: mf-run runs from a snapshot copy of the build
   output; worker skips stop for flagged services and restarts at the end
   (seconds of downtime). Snags: content root, ng serve live-reloading,
   agent may want the same port.
3. A separate "stable" instance: published build (dotnet publish + ng build)
   in its own folder, own ports, Tailscale points there; worker never
   touches it; updated by an explicit promote step. Debug stays localhost
   with today's stop/start. Matches the user's "small release version" idea.

## B: user's answer

Tailscale instance = the user themselves, checking a run's result; often
queues the next run right away, so the app is down again. Direction 3
chosen in principle. Frontend is usually compiled to static files inside
the backend (Triferto at /app, Wozzol at /woordjes-leren) → stable
instance = one process, one port. Wanted: opt-in, off by default; after a
cmd is done, build automatically, swap into place, and show on the
dashboard whether the new build is ready.

Side observation: cockpit Run card shows tailscale-host:7181 / :4204 while
the user actually browses https://<tailscale-host>/app/ (tailscale serve on
443). The shown links are not the one used.

## Premises      (B; all seven agreed 2026-10-09)

1. Opt-in per project: no `stable` config → nothing changes.
2. Stable = one published process (backend serving the built frontend),
   one port; Tailscale points at it. Debug services stay as today.
3. Auto-promote only after `done`, never after `aborted`/`questions`.
4. The publish runs inside the worker run, after the terminal commit,
   before the worker exits — so the next run can't edit the tree under it.
   Cost: the next run starts ~1–3 min later.
5. Publish goes to a staging folder; swap = stop old, move in new, start
   (seconds). A failed publish leaves the old stable running; warning in rst.
6. A small machine-local file records what stable runs (commit sha, time,
   state building/ready/failed); the cockpit shows it next to the lane head.
7. mf-run owns it (new verb, e.g. `mf-run promote`); worker only spawns it
   and reads the exit code — same pattern as start/stop.

## Premises      (A; all five agreed 2026-10-09)

1. Parallel streams are rare; a manual setup is fine, no tool change.
2. One watcher per project; the side stream runs by hand (/cmd-go).
3. The second working tree is a `git worktree`, not a second clone or a
   second cockpit project.
4. Renumbering at merge is worse than avoiding the clash: commit messages
   (`command 0042: …`), rst links, `Why: prompts/…` lines can't follow a
   rename. Avoid it at the source: the side branch starts at a clearly
   separate number (e.g. main at 0050 → side starts at 0100); gaps are free.
5. The side worktree has no Tailscale/HTTPS debug; local ports only.

## Directions (B)

1. Do nothing — debug app down during implement+build+test. Free.
2. Keep the debug instance running from a snapshot copy, restart at the
   end. Fights content root, ng serve, port reuse; still a debug build.
3. Opt-in stable instance, auto-promoted by `mf-run promote` after `done`
   (premises B1–B7). Reuses mf-run's process/PID logic and the worker's
   start/stop integration point. Recommended.
   Later variant if the wait hurts: publish from a git worktree of the
   done commit in the background (needs a supervised background process).

## Chosen

A: a habit, not a tool. Watcher on one branch; side stream in a
`git worktree`, by hand with /cmd-go, own number range, local debug only.
Possibly worth a short how-to (docs/context or project README), nothing more.
Notes: both worktrees may share the dev database (usually additive
migrations, accepted). Sourcetree (Windows) has no worktree management;
create/remove via CLI, open the worktree folder as its own repo tab.

B: direction 3, accepted. Extra wait is fine: optional, and implement+test
usually takes far longer than 1–3 min. Also makes the instance the user
checks more stable than today's debug app. The Run card should show the
link actually used (e.g. https://<tailscale-host>/app/).

## Open questions

- Config shape: a `run.stable:` block (publish command(s) with an output
  dir, command relative to the publish dir, args, url, optional link path
  like `/app/`) vs a flag on a service.
- Publish command per project: likely a wrapper script (like
  mf-start-api) taking the output dir, since dotnet publish + ng build +
  copying dist into wwwroot is project-specific.
- Where the stable folder lives: `.magnaflow/stable/{next,current,prev}`
  (machine-local, gitignored already) — keep `prev` for a manual rollback?
- Content root of the published app = publish dir (wwwroot copied by
  publish) — verify for Triferto's Help ContentPath.
- Cockpit: stable row on the Run card (sha, time, state, link) and a
  manual "Promote" button for runs done by hand (/cmd-go) — wanted?
- Does a manual `/cmd-go` run promote too, or only worker runs?
- The worker's terminal step order: start debug services, then promote,
  then exit; failed promote = `## Warning`, like a failed start.

## For the architect

- Read: docs/specs/run/mf-run.md, docs/specs/concepts/worker-run.md
  ("mf-run around the run"), docs/decisions/0010-mf-run-design.md (it put
  "anything deploy-shaped" out of scope — this is a deliberate,
  local-only extension: write a decision), docs/specs/cockpit/project.md#run.
- Decide first: config shape, then the state file format, then the
  cockpit display.
- Testbed: triferto-forecast (backend serves /app), then Wozzol.
- Part A needs no cmd; at most a short how-to on git worktree + separate
  cmd number range (docs/context or the kit README).

## For the architect
