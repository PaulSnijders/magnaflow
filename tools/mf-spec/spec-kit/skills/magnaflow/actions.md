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
   (default 30) are optional.
3. **For a script**, write both a `.cmd` and a `.sh` with the same base
   name, and name it in `actions.yml` without an extension. It runs in
   the project root and resolves per OS. Commit the `.sh` executable:
   `git update-index --chmod=+x scripts/actions/<name>.sh`.
4. **Check `.gitignore`** for `!.magnaflow/actions.yml` after
   `.magnaflow/*`. Without it the file works on one machine and never
   travels, and the cockpit warns about it.
5. **Commit** `actions.yml` and the scripts.
6. **Test once** with `mf-worker action <name>` in the project root. It
   refuses on a dirty tree, which is why you commit first. Exit 0 means
   ok, 1 failed or timed out, 2 a config error, and 3 refused.

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
  `HH:mm`. Each run is also bounded by the watcher's own worker timeout.

## Why did it not run?

- The cockpit's **Actions** card: schedule, last run with result, a newer
  refusal and its reason, and the log tail. **Run** is disabled while no
  watcher runs.
- `.magnaflow/actions/<name>.last.yml`: `run_at`, `trigger`, `result`,
  `duration_seconds`, plus `refused_at` and `refused` when a refusal is
  newer than the last run.
- `.magnaflow/actions/<name>.log`: every run's output between
  `=== action … ===` and `=== result: … ===`.
- `.magnaflow/mf-watch.log`: lines prefixed `[action <name>]`, refusals,
  and `actions.yml: <error>; actions skipped` when the file does not
  parse.
