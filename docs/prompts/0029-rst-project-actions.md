---
title: "Project actions: .magnaflow/actions.yml, mf-worker action, mf-watch schedules, cockpit Actions card"
cmd: 0029-cmd-project-actions.md
done: 2026-10-13
summary: "Project actions work end to end: mf-worker action, scheduling and requests in mf-watch, and the Actions card in the cockpit. To finish, add .magnaflow/actions.yml (resolve-conflicts, content in the rst) by hand, then roll out with install.ps1 and run the by-hand checks."
---

# Result

Done, apart from one dogfood file that the executor rules did not allow me to
write (see "Skipped"). `dotnet test`: worker-controller 228 passed, mf-watch
104 passed, mf-cockpit 355 passed, 0 failed. `spec_lint`: 0 problems.

I also ran the built `mf-worker action hello` against a scratch git repo with
an extensionless `tools/hello` that resolved to `hello.sh`. It gave exit 0,
wrote a log section with the header, the output and `=== result: ok — 0s ===`,
and wrote `last.yml`. After that I made the tree dirty: it gave exit 3, printed
`refused: the working tree is dirty`, added `refused_at`/`refused` to
`last.yml`, and wrote nothing to the log.

## What changed

### mf-worker (`tools/worker-controller`)
- **`Actions/` (new folder):**
  - **`ActionsFile`:** the YamlDotNet loader. It checks every rule from the concept, and each error names the action and the field. Errors are collected per file, joined with `; `.
  - **`ScriptResolver`:** a copy of mf-run's `CommandResolver`.
  - **`ActionLastRun`:** writes `last.yml` through `.tmp` plus `File.Move(overwrite)`. Times are local ISO-8601 with an offset. Writing a run drops the refusal fields. Writing a refusal keeps the run fields.
  - **`ActionLog`:** the path and the header/result line formats.
  - **`ActionRunner`:** the order from `worker/action.md`, behind the existing `IGitClient`, `IProcessRunner` and `IAgentRunner` seams plus an injectable clock.
- **`Commands/ActionCommand.cs`:** `action <name> [--project] [--trigger manual|schedule]`. Spectre runs with strict parsing, and a bad `--trigger` is a usage error with exit 2 through `Validate()`. It is registered in `WorkerCli`, with a usage line there and in the README.
- **`ProjectConfig.WithCommandTimeout`:** `ClaudeCodeRunner` takes its timeout from `config.CommandTimeout`, so a prompt action gets a config copy with its own `timeout_minutes`. `IAgentRunner` is unchanged.
- **The log is closed twice:** after the output (before the post-run dirty check), and again after the result line.

### mf-watch
- **`Actions/WatchActions`:** reads only the names and `every_minutes`/`at`, with the same checks on those fields. It also lists pending requests and reads `run_at` from `last.yml`.
- **`Actions/ActionSchedule`:** the due rules, as pure functions. The time zone is a parameter, so tests run in UTC.
- **`WatchLoop.RunActionsAsync`:** step 5, after dispatch and before the push.
- **`NextActionDue` and `SleepFor(interval)`:** together they give the shortened sleep. The `sleeping` log line shows the shortened value, rounded up to whole seconds.
- **`WatchLoop` constructor:** gained optional `IClock` and `TimeZoneInfo`. `Program.cs` passes the same `SystemClock` it gives the backoff.
- **`IGitClient.HeadAsync()`** is new and best effort. It is read only when an action was actually spawned, so a project without actions does exactly the git calls it did before.
- **Without `actions.yml`:** no new log lines, spawns, HEAD reads, sleep changes or notifications. There is a test for this, and stray request files are left alone.

### mf-cockpit
- **`Actions/ActionsFileLite`:** the full validation, copied, plus display text for the schedule and a `last.yml` reader.
- **`Api/ActionsEndpoints`:**
  - `GET /api/projects/{p}/actions` returns `ActionsDto { exists, error, ignored, actions[] }`. Each row has `name`, `description`, `kind`, `schedule`, `last` and `pending`.
  - `GET …/actions/{a}/log` returns a 100-line tail.
  - `POST …/actions/{a}/run` gives 404 unless the name is in the parsed file. Paths are built only from the parsed name. It gives 409 without a watcher (`IWatchControl.IsRunningAsync`) or while a request is pending. Otherwise it writes the request, then `MfWatchWakeFile.Write`, which leaves an existing wake file alone.
- **`IGitClient.IsIgnoredAsync`** runs `git check-ignore -q`. It never throws, and false means "no warning".
- **`project.html`:**
  - The Actions card sits below the Watcher card and stays hidden while `exists` is false.
  - Each row shows the name and description, the schedule, the last run with a newer refusal, *queued*, an inline error, **Run** and a log toggle.
  - **Run** is disabled while no watcher runs or a request is pending. Its title names `mf-worker action <name>`.
  - Open log tails survive a refetch.
  - The card re-renders after `loadWatch`, so Run follows the watcher's liveness. A live handler is registered for kind `actions`.
- **`ProjectWatcher`:**
  - The classifier is now `public static ClassifyMagnaflow(path, onStableState)`, so it can be tested.
  - `.magnaflow/actions.yml`, the `actions` directory and anything under it give `actions`. The check runs before the `evidence` fallback, and one path still gives one kind.
  - Side effect: an action's log no longer raises `evidence`, which `command.html` and `config.html` listen to. That is intended.
- **`ProjectScaffolder`:** `!.magnaflow/actions.yml` was added to the ignore lines, and the existing gitignore test now asserts it.

### Repo
- `!.magnaflow/actions.yml` was added to the root `.gitignore`.

## Specs
These are additions only, made where the specs left a choice open. Nothing in them contradicts what was written ahead.
- **`worker/action.md`:**
  - A script that doesn't resolve gives exit 2. The spec didn't cover this case, and it is a definition error like an unknown name.
  - The refusal is exactly one stdout line, `refused: <reason>`, with the two reason texts. mf-watch needs the reason in order to log once per reason, and stdout is the only channel between the tools.
  - A `started` line and the echo of script output to stdout (for a prompt, only the final text).
  - The stdout result line.
- **`watch/mf-watch.md` step 5:**
  - No spawn line of mf-watch's own. Otherwise a repeated refusal would leave two lines per poll, and the verify step asks for one.
  - A request for an unknown action, or one while `actions.yml` is invalid, is deleted and logged.
  - A failed delete is retried as a delete only. If it were left in place, the action would run again.
  - The refusal memory clears once the action runs.
  - The once-per-mtime rule for an invalid file.
- **`concepts/actions.md`:** the real paths on the `Code:` line.
- **`STATUS.md`:** a line under recent spec updates.

## Skipped / for the human
- **`.magnaflow/actions.yml` in this repo was not written.** The executor rules forbid creating anything under `.magnaflow/`, and that overrides the cmd's step 4. The `.gitignore` line is in place. Add and commit this file by hand:

  ```yaml
  actions:
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

  I added "abort and leave the tree clean" on a stop for a reason: the worker counts a tree left dirty as `failed`, and a stopped rebase would block every next command.
- **Not tested:** the Windows failed-delete retry, because a held-open file can't be produced on Linux in a unit test. It mirrors `WakeFile`, with `_undeletedRequests` per process.
- **The by-hand checks in Verify** need `install.ps1` and a live watcher. I didn't run them.
- **Exit-2 spam:** an action whose spawn exits 2 on every poll (say a scheduled action whose script was deleted) writes no `last.yml`. It stays due, so it notifies `action <name>: failed` on every regular poll. That is per spec, but it may be noisy. A notify-once rule is the obvious follow-up if it bites.

## Self-answered questions
- **The cmd asks for `.magnaflow/actions.yml` here, but the executor rules forbid `.magnaflow/`.** Answer: the rules win. The file content is above for the human to add.
- **A script that doesn't resolve: which exit code?** Exit 2, before the guards. It is a definition problem, like an unknown name, and no `last.yml` is written.
- **How does mf-watch learn the refusal reason, and how does a refusal become one log line?** The worker prints exactly one `refused: <reason>` line on stdout. mf-watch keeps those lines out of the live log, takes the reason from the captured stdout, and writes no spawn line of its own. The worker's `started` line, printed once past the guards, marks a real run.
- **Is "once per action and reason" forever?** No. The memory is cleared when the action gets past the guards. A tree that becomes dirty again later gets a new log line, like the pull-failure state.
- **A request for a name that isn't in `actions.yml`?** It is deleted and logged as `[action <name>] request skipped: no such action in actions.yml`, the same treatment as for an invalid file.
- **Order of several pending requests?** `actions.yml` order, then unknown names in ordinal order.
- **What does "retry the delete on the next poll" mean for the run?** Only the delete is retried. A request that already ran is never run again because its file survived.
- **Requested and due in the same poll?** The request runs first. `last.yml` is re-read before the due check, so the scheduled run doesn't follow straight after.
- **What counts as "not refused" for the sleep minimum?** Only due times after now count. A due time already past (refused, or skipped because the pull failed) would spin the loop, so it waits for the next regular poll. The shortened sleep is at least 1 s.
- **Local time for `at`.** mf-watch's `IClock` is UTC. The due rules convert through an injected `TimeZoneInfo`, which defaults to `Local`. Tests use UTC and a custom +2 zone.
- **Timeout for a prompt action.** It comes from `timeout_minutes`, through a config copy (`WithCommandTimeout`). `IAgentRunner` keeps its signature.
- **Stdout of a by-hand `mf-worker action`.** Script output is echoed live. A prompt echoes only its final text, because the raw stream-json goes to the log.
- **Seconds in the result line and `duration_seconds`.** Whole seconds, rounded.
- **Run's 409 body.** It uses the existing `WatchActionResponseDto { success:false, error }`, so the page shows the reason inline.
