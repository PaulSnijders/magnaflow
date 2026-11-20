---
title: "Action condition: a scheduled action runs only when its condition holds"
status: done
created: 2026-11-20
attempts: 1
---

## Context

A project wants a nightly prompt action (the "dream" skill: an agent
that tidies the specs and writes a report) to run only after a day on
which the project was worked on. Today that check can only live in the
prompt, so an agent session starts every night just to find out it has
nothing to do. This cmd adds an optional `condition` to an action: a
cheap script, evaluated before a scheduled run, whose exit code decides
run / skip / error.

The specs are written ahead and are the contract. Read them first:

- `docs/specs/concepts/actions.md` — the field (file section), the due
  rule ("Last run" = later of `run_at` and `skipped_at`), the new
  **Condition** section, the Files section (log block, `last.yml`).
- `docs/specs/worker/action.md` — `--trigger`, Order step 4 (new), exit
  codes.
- `docs/specs/watch/mf-watch.md` — One poll step 5, Notifications.
- `docs/specs/cockpit/project.md#actions` — the row.

Design choices settled here, so the run does not have to:

- **The worker evaluates the condition, not mf-watch.** The guards live
  in `mf-worker action`, and the condition must come after them; the
  worker already has the script resolver and the process seam. mf-watch
  stays a reader of exit codes and one stdout line, exactly as with
  `refused:`.
- **Only `--trigger schedule` evaluates it.** `manual` (button press,
  by hand) and `conflict` never do. No new CLI flag.
- **Exit codes stay within the shared table**: skip is 0 (nothing to do
  is success), condition error is 1. The stdout line `skipped: <reason>`
  tells mf-watch which one it is.
- **A condition error settles the slot like a skip** and notifies once.
  Otherwise a broken condition on an `at` action would spawn, error and
  notify on every poll all day.
- **Fixed timeout 30 s**, not configurable (same as the notifier).
- **Shape**: `condition` + `condition_args`, mirroring `script` + `args`
  and reusing `ScriptResolver`. An argv-without-shell form was
  considered; it adds a second way to name a program for no gain, since
  scripts already run without a shell through `RunExecutableAsync`.

## Task

### 1. mf-worker (`tools/worker-controller/`)

- `Actions/ActionsFile.cs`: `ActionDefinition` gains `string? Condition`
  and `IReadOnlyList<string> ConditionArgs` (empty when absent). DTO
  fields `Condition`, `ConditionArgs`. Validation, through the existing
  `Problem(...)` helper and message style:
  - `condition` present but empty/whitespace → `condition`, "must name a
    script".
  - `condition_args` without `condition` → `condition_args`, "needs
    condition".
  - `condition` on an action with neither `every_minutes` nor `at` →
    `condition`, "needs every_minutes or at (only a scheduled run
    evaluates it)".
- `Actions/ActionLastRun.cs`: add `SkippedAt` and `Skipped`, saved as
  `skipped_at` / `skipped` (quoted like `refused`), after the refusal
  fields. Loading a file without them is unchanged. A run still writes a
  fresh record (both refusal and skip fields null).
- `Actions/ActionRunner.cs`: new step between the guards and the run,
  only when `trigger == "schedule"` and `action.Condition is not null`:
  1. Open the action log, write `ActionLog.Header(name, trigger, start)`.
  2. Resolve `Path.Combine(projectRoot, action.Condition)` with
     `ScriptResolver`. Not found → reason `condition error (not found)`;
     also append one log line naming the paths tried.
  3. Else `processes.RunExecutableAsync(path, action.ConditionArgs,
     projectRoot, appendToLogOnly, TimeSpan.FromSeconds(30))`. Exception
     on start → `condition error (exit -1)`, like `RunScriptAsync`.
     Timeout → `condition error (timeout)`. Exit 0 → continue. Exit 1 →
     `condition`. Other → `condition error (exit N)`.
  4. On continue: the header is already written, so the existing step
     must not write it again (restructure so one block holds both).
     Then `action <name> (schedule): started` and the run as today.
  5. On skip/error: write `ActionLog.ResultLine(<result>, seconds)` with
     `<result>` = `skipped (condition)` or the error reason; save
     `last with { SkippedAt = start, Skipped = reason }` (load first, keep
     run and refusal fields); stdout exactly `skipped: <reason>`; return
     0 for `condition`, 1 for an error.
  Put the `skipped: ` prefix in a constant next to `RefusedPrefix`.
- Step 2 of the order (what the action needs) is unchanged: a manual run
  must not need its condition to resolve.

### 2. mf-watch (`tools/mf-watch/`)

- `Actions/WatchActions.cs`: replace `LastRunAt` by `LastSettledAt`
  (later of `run_at` and `skipped_at`; either missing → the other; both
  missing → null) and use it in both call sites in `Watch/WatchLoop.cs`.
  Mirror the three validation rules above (DTO gains `Condition`,
  `ConditionArgs`), so an invalid condition gives the existing
  `actions.yml: <error>; actions skipped` once, instead of a worker exit
  2 on every poll.
- `Watch/WatchLoop.cs` (the action spawn, ~line 380): collect a
  `skipped: ` line like the `refused: ` one. Exit 0 + skipped line →
  nothing beyond the normal output log line; it is not activity. Exit 1
  + `skipped: condition error …` → notify title `action <name>:
  condition error`, message the reason, instead of `failed`. Every other
  exit path unchanged.

### 3. mf-cockpit (`tools/mf-cockpit/`)

- `Actions/ActionsFileLite.cs`: same three validation rules (the card
  shows the error inline like the others); the schedule text gets
  ` · if <condition>` (as written in the file) when set; `LastDto` and
  the returned last-run model gain `skipped_at` / `skipped`.
- `Models/Dtos.cs` and `wwwroot/project.html`: render a skip newer than
  the last run as `skipped <time>: <reason>` — muted for `condition`,
  the failed-result style for `condition error …`. Same placement as the
  newer refusal.

### 4. Kit: the `magnaflow` skill (`tools/mf-spec/spec-kit/skills/magnaflow/`)

- New `conditions/commits-since.sh` and `conditions/commits-since.cmd`:
  `commits-since <hours> [prefix...]`.
  - `git log --since="<hours> hours ago" --format=%s HEAD`; drop every
    subject that starts with one of the prefixes (literal,
    case-sensitive).
  - One stdout line, e.g. `3 commits in the last 16h (2 ignored)` or
    `no commits in the last 16h (2 ignored)`.
  - Exit 0 when at least one commit remains, 1 when none, 2 when
    `<hours>` is not a positive integer or git fails.
  - `.cmd` without PowerShell: a `findstr /b /l /v /c:"<prefix>"…` filter
    built from the arguments is fine. Commit the `.sh` executable
    (`git update-index --chmod=+x`).
- `actions.md`:
  - "Making one": a step for an optional `condition` (+
    `condition_args`): only with a schedule; exit 0 run, 1 skip, other
    or 30 s timeout = condition error; the button and `mf-worker action`
    by hand ignore it; test it with `mf-worker action <name> --trigger
    schedule`.
  - Examples: the `dream-learn` example from
    `docs/specs/concepts/actions.md`, and how to install the shipped
    condition: copy both files from `.claude/skills/magnaflow/conditions/`
    to `scripts/actions/`, `git update-index --chmod=+x` the `.sh`,
    commit. (Copied, not referenced in place: the skill folder is
    overwritten on update and an unzip may drop the exec bit.)
  - Rules: a skip settles the slot (next day / next interval); a
    condition error too, and it notifies.
  - "Why did it not run?": `skipped_at` / `skipped` in `last.yml`, the
    skip on the Actions card, the `=== result: skipped (condition) ===`
    block in the action log, `skipped:` lines in mf-watch.log.
- `SKILL.md`: only if its summary of actions lists the fields, add
  `condition`.
- `KIT.md` contents row for `skills/magnaflow/`: add `conditions/`. No
  version bump: the update already overwrites the folder.
- Then run `tools/mf-spec/spec-kit/0002-update-spec-system.md` on this
  repo and rebuild `tools/mf-spec/spec-kit.zip` as the root `CLAUDE.md`
  describes, in the same commit.

## Must not change

- An `actions.yml` without `condition` behaves exactly as before: same
  log, same `last.yml`, same stdout, same exit codes.
- Guards first: a refusal is decided before the condition runs, is not
  recorded as a skip, and is retried at the next poll.
- `manual` and `conflict` triggers never run the condition.
- `refused:` handling, the conflict flow and dispatch are untouched.

## Verify

- xUnit, worker:
  - `ActionsFileTests`: condition + args parsed; the three invalid
    cases; a file without the field parses as before.
  - `ActionLastRunTests`: `skipped_*` round-trip; a run record has none.
  - `ActionRunnerTests` (fake `IProcessRunner` recording calls):
    schedule + condition exit 0 → action runs, one log block with the
    condition output first; exit 1 → action not started, exit 0,
    stdout `skipped: condition`, `last.yml` has `skipped_*` and the
    earlier `run_at` kept; exit 2 → exit 1, `condition error (exit 2)`;
    timeout → `condition error (timeout)`; missing script →
    `condition error (not found)`; `manual` and `conflict` → condition
    never called (the button-press and by-hand case); dirty tree +
    condition → refused, condition never called; no condition +
    schedule → unchanged.
- xUnit, mf-watch:
  - `ActionScheduleTests` / `WatchActions`: `skipped_at` today after
    HH:mm → `at` not due until tomorrow; `skipped_at` newer than
    `run_at` settles `every_minutes`; only `run_at` → as before.
  - `WatchLoopActionsTests`: stub worker exits 0 with `skipped:
    condition` → no notification, no activity; exits 1 with `skipped:
    condition error (exit 2)` → `action <name>: condition error`, no
    `failed`; invalid `condition` in `actions.yml` → `actions skipped`
    logged once.
- xUnit, cockpit: schedule text with condition; skip fields in the
  model; the validation error inline.
- Kit script, by hand in a scratch repo, on Windows (`.cmd`) and in Git
  Bash or Linux (`.sh`): commits only with `dream:` → exit 1; one real
  commit → exit 0; `abc` as hours → exit 2. Note the results in the rst.
- `node scripts/spec_lint.mjs` green; `/spec-drift` green.
- **Human, after the self-update / `install.ps1`**: in a project, add a
  `condition` to an `every_minutes: 2` test action whose condition exits
  1; watch one skip appear on the Actions card and in `last.yml`, not
  every poll; switch it to exit 2 and see the `condition error`
  notification; press Run and see it run regardless.

## Not in scope

- A configurable condition timeout — 30 s is enough for a "cheap"
  check; add one when a project hits it.
- Conditions for manual or `on_conflict` runs — a human or the conflict
  already decided.
- Built-in condition types in YAML (`commits_since: 16h`) — one script
  convention instead of a mini-language.
