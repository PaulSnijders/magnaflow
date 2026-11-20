# Actions (`.magnaflow/actions.yml`)

An action is a named job for this project: a **script**, or a fixed
**prompt** for the agent. It is started by its button on the cockpit's
Actions card or on a schedule. The watcher runs it between cmds, so it
never overlaps a worker run.

## Making one

1. **Pick the kind and the schedule.**
   - `script`: deterministic work (sync a board, check something, write a
     cmd). This is the default choice.
   - `prompt`: judgement that is not a code change, such as resolving a
     rebase conflict or triage. It runs the project's `agent` from
     `config.yml` in a fresh session.
   - Schedule: `every_minutes: N`, or `at: "HH:mm"` (local time, daily),
     or neither for the button only. Never both.
2. **Write the block** under `actions:`. The name matches
   `[a-z0-9][a-z0-9-]*`. Give exactly one of `script` and `prompt`.
   `description`, `args` (a list, passed as argv) and `timeout_minutes`
   (default 30) are optional, and so is a `condition` (step 4).
3. **For a script**, write both a `.cmd` and a `.sh` with the same base
   name, and name it in `actions.yml` without an extension. It runs in
   the project root and resolves per OS. Commit the `.sh` executable:
   `git update-index --chmod=+x scripts/actions/<name>.sh`.
4. **Optionally, a `condition`** (with `condition_args`, a list passed
   as argv): a cheap script that decides whether a *scheduled* run is
   worth starting, so an agent session does not start just to find
   nothing to do. Only with `every_minutes` or `at`. It resolves like
   `script`, runs in the project root before the action, with a fixed
   30 s timeout. Exit 0: the action runs. Exit 1: skip. Anything else, a
   timeout or a script that is not found: condition error. The button
   and `mf-worker action <name>` by hand ignore it; test it with
   `mf-worker action <name> --trigger schedule`.
5. **Check `.gitignore`** for `!.magnaflow/actions.yml` after
   `.magnaflow/*`. Without it the file works on one machine and never
   travels, and the cockpit warns about it.
6. **Commit** `actions.yml` and the scripts.
7. **Test once** with `mf-worker action <name>` in the project root. It
   refuses on a dirty tree, which is why you commit first. Exit 0 means
   ok (or skipped by its condition, stdout `skipped: condition`), 1
   failed, timed out or a condition error, 2 a config error, and 3
   refused.

## Examples

A script with a button only:

```yaml
actions:
  git-status:
    description: Show branch, ahead/behind and the last commits
    script: scripts/actions/git-status
    timeout_minutes: 1
```

`scripts/actions/git-status.sh` (committed executable):

```sh
#!/bin/sh
git status --short --branch
git log --oneline -5
```

`scripts/actions/git-status.cmd`:

```bat
@echo off
git status --short --branch
git log --oneline -5
```

A prompt:

```yaml
  resolve-conflicts:
    description: Finish a git pull --rebase that git_sync gave up on
    prompt: |
      Run `git pull --rebase`. Resolve every conflict: read both sides and
      keep the intent of each. Then build and test with the build and test
      commands in .magnaflow/config.yml, finish the rebase
      (`git rebase --continue`) and `git push`.
      If a conflict is not clearly resolvable, or the build or tests fail
      after resolving, stop: run `git rebase --abort`, leave the tree clean,
      and explain what conflicted and why you stopped.
```

A nightly prompt that only runs after a day of work, through the
condition the skill ships:

```yaml
  dream-learn:
    at: "02:00"
    condition: scripts/actions/commits-since
    condition_args: ["16", "dream:", "review:", "lane:"]
    prompt: |
      Follow .claude/skills/dream/SKILL.md as `/dream learn`.
```

`commits-since <hours> [prefix...]` exits 0 when `HEAD` has a commit
from the last `<hours>` hours whose subject starts with none of the
prefixes (literal, case-sensitive), 1 when it has none, 2 on bad usage
or a git failure, and prints one line such as `3 commits in the last 16h
(2 ignored)`. So the action's own commits and bookkeeping do not count
as work. To install it, copy both files from
`.claude/skills/magnaflow/conditions/` to `scripts/actions/`, run
`git update-index --chmod=+x scripts/actions/commits-since.sh` after
`git add`, and commit. Copy it rather than pointing at the skill
folder: an update overwrites that folder, and an unzip may drop the
exec bit.

The `resolve-conflicts` prompt above also runs by itself: the watcher's `on_conflict` setting
(default `resolve-conflicts`) names the action it runs, with `--trigger
conflict`, as soon as a `git_sync` pull stops on a rebase conflict. A
second pull is the verdict. If it succeeds the watcher notifies
`mf-watch: git conflict resolved` (look at what the agent kept);
otherwise the usual conflict notification names the action's log, and
the same conflict state is not tried again. Without such an action, or
with `on_conflict: null` in the watcher's config, a conflict waits for a
human.

## Rules

- **Guards**: an action is refused, and nothing runs, while a cmd in the
  lane is `running` or the working tree is dirty. A refusal does not
  count as a run. A scheduled action is retried at the next poll.
- **Leave the tree clean.** An action that leaves changes or untracked
  files behind counts as failed, even with exit 0. Write scratch output
  under a gitignored path, or nowhere.
- **It may commit.** Pushing is not needed: on a machine with `git_sync`
  on, the watcher pushes after the poll.
- **Code changes go through the lane.** An action that wants code changed
  writes a cmd file in `docs/prompts/` with `status: ready` (and
  optionally `branch:`, so that a human merges the fix) and commits it.
  The next poll dispatches it. A prompt action does not change code
  itself.
- **When it runs**: a button press runs at the next poll, even when the
  pull failed. A schedule runs only on a poll whose pull succeeded (or
  with `git_sync` off). `at` catches up once if the watcher was down at
  that time. A new machine does not fire an `at` job before its first
  `HH:mm`. The `on_conflict` action also runs on a rebase conflict. Each run is also bounded by the watcher's own worker timeout.
- **A condition skip settles the slot**: an `at` action is next due the
  following day, an `every_minutes` action after N minutes. A skip is
  not a failure and does not notify. A condition error settles the slot
  too, so a broken condition errors once per slot, and it notifies
  `action <name>: condition error`. A refusal is decided before the
  condition runs and is retried at the next poll, as before.

## Why did it not run?

- The cockpit's **Actions** card: schedule (with `· if <condition>`),
  last run with result, a newer refusal and its reason, a newer skip
  (`skipped <time>: condition` or `condition error (…)`), and the log
  tail. **Run** is disabled while no
  watcher runs.
- `.magnaflow/actions/<name>.last.yml`: `run_at`, `trigger`, `result`,
  `duration_seconds`, plus `refused_at` and `refused` when a refusal is
  newer than the last run, and `skipped_at` and `skipped` (`condition`,
  or `condition error (exit N|timeout|not found)`) when a condition skip
  is.
- `.magnaflow/actions/<name>.log`: every run's output between
  `=== action … ===` and `=== result: … ===`. A condition's output comes
  first in the block; a skip ends it with `=== result: skipped
  (condition) … ===` or the `condition error (…)` reason.
- `.magnaflow/mf-watch.log`: lines prefixed `[action <name>]`, refusals,
  `skipped:` lines from a condition,
  and `actions.yml: <error>; actions skipped` when the file does not
  parse.
