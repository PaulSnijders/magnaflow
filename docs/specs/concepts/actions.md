# Technical

Actions are a project's own named jobs: a script, or a fixed prompt for
the agent, started from the cockpit or on a schedule. They are an
optional extension. A project without `.magnaflow/actions.yml` behaves
exactly as before. mf-watch decides *when* an action runs, `mf-worker
action` runs it ([worker action](../worker/action.md)), and the cockpit
shows it and requests a run ([project page](../cockpit/project.md#actions)).

## The file

`.magnaflow/actions.yml`, committed, identical on every machine:

```yaml
actions:
  check-board:
    description: Sync the board, queue new items as cmds   # optional
    script: tools/check-board        # resolved against the project root
    args: ["--assignee", "paul"]     # optional, passed as argv
    every_minutes: 10                # optional schedule
    timeout_minutes: 30              # optional, default 30
  nightly-deps:
    script: tools/update-deps
    at: "02:30"                      # optional schedule, local time
  dream-learn:
    at: "02:00"
    condition: scripts/actions/commits-since   # optional, schedule only
    condition_args: ["16", "dream:", "review:", "lane:"]
    prompt: |
      Follow .claude/skills/dream/SKILL.md as `/dream learn`.
  resolve-conflicts:
    prompt: |
      Run git pull --rebase. Resolve every conflict, build and test,
      finish the rebase and git push.
```

- A name matches `[a-z0-9][a-z0-9-]*`; it is used in file names.
- Exactly one of `script` and `prompt`. `script` resolves like an mf-run
  `command`: the literal first, then per OS `.cmd`/`.bat`/`.exe` or
  `.sh` ([mf-run](../run/mf-run.md#config)). `prompt` is sent as is to
  the project's `agent.command`/`agent.args` from `config.yml`, in a
  fresh session.
- At most one of `every_minutes` (positive integer) and `at` (`HH:mm`).
  Neither means manual only.
- `condition` (optional, for both kinds) names a script that resolves
  exactly like `script`, with an optional `condition_args` list passed
  as argv. It needs `every_minutes` or `at`, because only a scheduled
  run evaluates it, see [Condition](#condition). `condition_args`
  without `condition` is invalid, and so is an empty `condition`.
- `.magnaflow/*` is gitignored, so a project that uses actions needs
  `!.magnaflow/actions.yml` in its `.gitignore`. A new project from the
  cockpit gets the line. Without it the file works on one machine and
  never travels; the cockpit warns about that.

## When an action runs

mf-watch is the only scheduler, and it runs actions inside its poll,
after dispatch and before the push
([one poll](../watch/mf-watch.md#one-poll)). Its loop is single-threaded,
so an action never overlaps a worker run and needs no lock. Queued work
waits for the next poll.

- **Manual**: the cockpit writes `.magnaflow/actions/<name>.request`
  (empty) and the wake file. Every poll runs each pending request once and
  then deletes the file, whatever the outcome. Requests also run when
  the pull failed: the human asked, and resolve-conflicts exists for
  exactly that state.
- **Scheduled**: only on a poll whose pull succeeded, or with `git_sync`
  off. "Last run" below means the later of `run_at` and `skipped_at` in
  `last.yml`, so a [skip](#condition) settles its slot like a run. Due
  means:
  - `every_minutes: N`: the last run started N minutes ago or more, or
    there is no run yet.
  - `at: HH:mm`: the last run started before the most recent HH:mm
    (local time). Without a run, the watcher's start time counts as the
    last run, so a new machine does not fire a nightly job at noon. A
    watcher that was down at HH:mm catches up once at its next poll.
- **On a conflict**: the action named by mf-watch's `on_conflict`
  (default `resolve-conflicts`) runs as soon as a `git_sync` pull stops
  on a rebase conflict, once per conflict state, whether or not it has
  a schedule. A second pull decides whether it worked, see
  [git sync](../watch/mf-watch.md#git-sync). A project without that
  action keeps the plain conflict notification.
- A due action shortens mf-watch's sleep to its due time. A refused
  action (below) is retried at the next regular poll, never in a tight
  loop.
- "Run" means it got past the guards and its condition. A refusal does
  not count as a run, and is retried. A skip does not count as a run
  either, but it settles the slot.

## Condition

A scheduled action with a `condition` runs only when the condition
holds. So "is there anything to do?" lives in the schedule, and no agent
session starts just to find out that there is not.

- Only `--trigger schedule` evaluates it. A button press, a
  `mf-worker action <name>` by hand (default trigger `manual`) and the
  `on_conflict` run never do: a human asked, or the conflict is the
  reason.
- `mf-worker action` evaluates it after the [guards](#guards) and before
  the script or agent starts. A refusal still comes first and is
  retried as before.
- It runs in the project root through the process seam, like a script
  action, with a fixed timeout of 30 s. Its output goes to the action
  log.
- Exit 0: the action runs, in the same log block. Exit 1: **skip**.
  Anything else, a timeout, or a condition script that does not resolve:
  **condition error**. The action does not run in either case.
- A skip and a condition error both write `skipped_at` and `skipped` to
  `last.yml` (`condition`, or `condition error (exit N)`,
  `condition error (timeout)`, `condition error (not found)`). Both
  settle the slot: an `at` action is next due the following day, an
  `every_minutes` action after N minutes. A broken condition therefore
  errors once per slot, not on every poll.
- A skip is not a failure and does not notify. A condition error
  notifies `action <name>: condition error`
  ([mf-watch](../watch/mf-watch.md#notifications)).
- A condition must not change the tree. It is not checked for that: it
  is a read-only question, and the guards run again before the next
  command anyway.

## Guards

`mf-worker action` refuses (exit 3, nothing run) while any command in
the lane is `running`, or while the working tree is dirty. Inside
mf-watch neither happens during its own worker run. What is left is a
stale `running`, a `/cmd-go` by hand, or a human mid-edit, and in those
cases the action must not touch the tree. mf-watch logs a refusal once
per action and reason and does not notify.

After the run the worker checks the tree again. An action that leaves it
dirty has failed: a dirty tree would make the worker refuse every next
command.

## What an action may do

Anything a human could do in the working copy. It may commit. Pushing
is optional, because the poll's `git_sync` push follows. Code changes
by the agent belong in the lane: a script action writes a cmd file
(`status: ready`, optionally with `branch:` so that a human merges an
automatic fix) and commits it, and the next poll dispatches it. A
`prompt` action is meant for work that is not a cmd, such as resolving
a rebase conflict or triage that writes nothing to the tree.

## Files

| File | In git | Written by | Holds |
|---|---|---|---|
| `.magnaflow/actions.yml` | yes | human | the definitions |
| `.magnaflow/actions/<name>.request` | no | cockpit, `touch` | run once, at the next poll |
| `.magnaflow/actions/<name>.log` | no | mf-worker | every run, appended |
| `.magnaflow/actions/<name>.last.yml` | no | mf-worker | the last run and the last refusal |

The log gets one header per run, `=== action <name> (manual|schedule|conflict) —
yyyy-MM-ddTHH:mm:ss ===`, then the merged output (for a prompt, the
agent's raw output, followed by its final text), then
`=== result: <result> — <seconds>s ===`. `<result>` is `ok`,
`failed (exit N)`, `failed (left the tree dirty)` or `timeout`. With a
condition, the condition's output comes first in the block; a skip or a
condition error ends the block with `skipped (condition)` or the
`condition error (…)` reason as `<result>`.

`last.yml` holds `run_at`, `trigger`, `result` and `duration_seconds`
for the last run, plus `refused_at` and `refused` (the reason) for a
refusal newer than that run, and `skipped_at` and `skipped` (the reason)
for a skip or condition error newer than that run. A run clears both.
mf-watch reads `run_at` and `skipped_at` to decide what is due, and the
cockpit shows the rest. Missing means never run.

The kit's `magnaflow` skill ships one ready-made condition,
`commits-since`: exit 0 when `HEAD` has a commit from the last N hours
whose subject starts with none of the given prefixes, so an action's own
commits and bookkeeping do not count as work.

Code: tools/worker-controller/src/MagnaFlow.WorkerController/Actions/, tools/worker-controller/src/MagnaFlow.WorkerController/Commands/ActionCommand.cs, tools/mf-watch/src/MagnaFlow.MfWatch/Actions/, tools/mf-watch/src/MagnaFlow.MfWatch/Watch/WatchLoop.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Actions/ActionsFileLite.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Api/ActionsEndpoints.cs
Why: decisions/0019-actions-run-by-watch.md
