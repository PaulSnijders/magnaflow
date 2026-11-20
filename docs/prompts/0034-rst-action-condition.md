---
title: "Action condition: a scheduled action runs only when its condition holds"
cmd: 0034-cmd-action-condition.md
done: 2026-11-20
summary: "Actions can now have a condition script (condition + condition_args) that a scheduled run checks first: exit 0 runs, 1 skips, anything else is a condition error that notifies once per slot. The kit ships commits-since; please try the .cmd on Windows and do the human check from the cmd after the self-update."
---

# 0034 — Action condition: result

## What was done

- **mf-worker**
  - `ActionsFile`: `ActionDefinition` gains `Condition` and `ConditionArgs`, with the three validation rules.
  - `ActionLastRun`: adds `SkippedAt` / `Skipped`, saved as `skipped_at` / `skipped` (quoted) after the refusal fields.
  - `ActionRunner`:
    - The header is now written once, at the top of one log block. After the guards, a `schedule` run with a condition first calls `EvaluateConditionAsync`: it resolves the script with `ScriptResolver` and runs it with a 30 s timeout, its output going to the log only.
    - On a skip or a condition error, the runner writes the result line and saves `last with { SkippedAt, Skipped }`. It prints `skipped: <reason>` and returns 0 for `condition`, 1 for an error.
    - `SkippedPrefix` sits next to `RefusedPrefix`.
    - Without a condition, the log, stdout and `last.yml` are the same as before.
- **mf-watch**
  - `WatchActions.LastRunAt` is replaced by `LastSettledAt` (the later of `run_at` / `skipped_at`) at both call sites, and the three validation rules are mirrored.
  - `RunActionAsync`:
    - Exit 0 with a `skipped:` line returns without the `finished: ok` line. The worker's `skipped:` line still reaches the log through the output callback.
    - Exit 1 with `skipped: condition error …` notifies `action <name>: condition error` (the message is the reason), with no `failed` log line or notification.
- **mf-cockpit**
  - `ActionsFileLite` has the three rules (they show as the inline error) and adds ` · if <condition>` to the schedule text.
  - `ActionLastLite` / `ActionLastDto` carry `SkippedAt` / `Skipped`.
  - `project.html` renders `skipped <time>: <reason>` under the refusal line: muted for `condition`, the new `.action-error` class (`--status-aborted` red) for `condition error …`.
- **Kit**
  - New `skills/magnaflow/conditions/commits-since.sh` (POSIX sh, committed `100755`) and `commits-since.cmd`.
  - `actions.md`:
    - a new step 4 for the condition, with the steps after it renumbered
    - the step 7 exit codes now include skip and condition error
    - the `dream-learn` example and how to install the shipped condition
    - a Rules bullet
    - "Why did it not run?" entries
  - `KIT.md` row: adds `conditions/`. No version bump.
  - `SKILL.md` is unchanged: it does not list the fields.
- **Dogfooding**
  - Ran the 0002 update: every other installed copy was already byte-identical to the master, and the CLAUDE.md managed section was identical.
  - The installed `.claude/skills/magnaflow/` had no project edits (identical to the old master). It is now overwritten from the master, including `conditions/`.
  - `spec-kit.zip` is rebuilt with the CLAUDE.md recipe. This staged the kit and skill files in the index (part of the recipe), and nothing was committed.
- The specs were written ahead and needed no change. `node scripts/spec_lint.mjs`: 0 problems.

## Tests

- **Worker** (266 pass):
  - `ActionsFileTests`: parse, the three invalid cases, no condition gives an empty `ConditionArgs`.
  - `ActionLastRunTests`: skip round-trip and field order; a run record has no skip fields.
  - `ActionRunnerTests`:
    - exit 0 runs in one block, condition output first, with the 30 s timeout
    - exit 1 gives exit 0 and keeps the earlier `run_at`
    - a prompt action skips before the agent starts
    - exit 2 and a timeout are condition errors
    - an exception gives exit -1
    - a missing script is `not found`, with the paths tried
    - `manual` and `conflict` never call it, even when the condition doesn't resolve
    - a dirty tree refuses before the condition
    - no condition: unchanged
  - `FakeProcessRunner` gained the opt-in `EchoExecutableStdOut` and `ExecutableTimeouts`.
- **mf-watch** (131 pass):
  - `ActionScheduleTests`: three `LastSettledAt` + schedule tests.
  - `WatchLoopActionsTests`:
    - a skip: no notification, no activity, no `finished`
    - a condition error: only `condition error`
    - an invalid condition: `actions skipped` logged once
- **Cockpit** (397 pass): schedule text with the condition, skip fields in the API model, and the three validation errors inline.
- **Kit script `.sh`, by hand in a scratch repo on Linux (dash as `/bin/sh`)**:
  - only `dream:` / `lane:` commits gives exit 1, `no commits in the last 16h (2 ignored)`
  - adding `Dream: real work` gives exit 0, `1 commit …`, which also shows the prefix match is case-sensitive
  - `abc`, `0`, `-3` or no argument gives exit 2
  - a repo without `HEAD` gives exit 2 (git's error plus `git log failed`)
- **Kit script `.cmd`: NOT tested.** I'm on Linux with no Windows or wine. That test is still open: on Windows, run the same three cases (only `dream:` → 1, one real commit → 0, `abc` → 2).

## Decisions taken while implementing

- **The `.cmd` has no labels or `goto`.** `git archive` of the `spec-kit/` subtree does not see the root `.gitattributes`, so the zip carries the `.cmd` with LF endings, and cmd.exe can misfind labels in LF-only files. The repo copy is CRLF as `.gitattributes` says. I didn't change the documented zip recipe.
- **One `findstr /b /l /v /c:"<prefix>"` per prefix** over a temp file in `%TEMP%`, not one findstr with several `/c:` strings. findstr has a known bug where several case-sensitive literals of different lengths can miss matches. Prefixes are taken from `for %%p in (%*)`, so a prefix must not contain `*` or `?` (noted in the script).
- **Output wording:** `1 commit` for a single commit, `N commits` otherwise, `no commits` for none, as in the cmd's examples.
- **Timestamps:** `start` (used for the log header, `skipped_at` and the run's `run_at`) is taken once, just after the guards, so a run's duration includes its condition.
- **Cockpit style:** the cockpit had no failed-result style for actions (the result was plain text), so I added `.action-error { color: var(--status-aborted); }` next to `.run-result.run-error`.
- **Missing condition:** the log line reads `condition not found: tried <paths>`, the same shape as the worker's `script not found` error.

## Self-answered questions

- **Where `start` is taken:** once, before the condition. `skipped_at` and the run's `run_at` both use it.
- **Does mf-watch log `finished: ok` after a skip?** No. "Nothing beyond the normal output log line" means only the worker's `skipped: condition` line.
- **Does mf-watch also log `failed (exit 1)` on a condition error?** No. Only the worker's `skipped:` line and the `condition error` notification.
- **A refusal after a skip (or the reverse):** each is written onto the loaded record, so the other survives. A real run clears both.
- **`condition:` with a YAML null value:** treated as absent, so `condition_args` beside it gives "needs condition". `condition: ""` or whitespace gives "must name a script".
- **Is the condition trimmed?** Yes, like `script`. The cockpit shows it trimmed.
- **What "the failed-result style" is in the cockpit:** there was none for actions, so I added `.action-error` in the aborted red.
- **How the skip shows on the card:** whenever `skipped_at` is present. The worker clears it on every run, so it is always newer than the last run, which is the same logic as the refusal line.

## Open / for the human

- Run the `.cmd` tests on Windows, listed under Tests.
- The human check from the cmd's Verify, after the self-update or `install.ps1`:
  1. Add a condition that exits 1 to an `every_minutes: 2` action and check that one skip appears on the card and in `last.yml` per slot.
  2. Switch it to exit 2 and check for the `condition error` notification.
  3. Press Run and check that it runs regardless of the condition.
- I didn't run `/spec-drift` as a separate skill. The specs were ahead, and I checked the code against each spec bullet by hand.
