---
title: "Opt-in stable instance: mf-run promote, worker promotes after done, cockpit stable row"
cmd: 0027-cmd-stable-instance.md
done: 2026-10-09
summary: "New opt-in run.stable: mf-run promote publishes into .magnaflow/stable/, swaps and restarts a stable instance; the worker promotes after a branchless done; the cockpit's Run card gets a stable row with Promote. Projects without run.stable are unchanged. Roll out with install.ps1 by hand, then do the triferto-forecast check from the cmd's Verify."
---

# Result

Done. All three suites pass: mf-run 84, worker-controller 199, mf-cockpit 330. `spec_lint` reports 0 problems.

I also ran the real binaries on Linux against a scratch git project. The stable app was a `python3 -m http.server` wrapper published by a `pub.sh`. Checked:
- `promote` the first time and again: swap done, `prev/` kept, new PID;
- `status` as text and as JSON;
- `stop` without a name leaves stable running;
- `status stable` and `stop stable` work.

I drove the cockpit page in headless Chrome over CDP:
- the stable row shows `ready`, the short sha, **behind** after a new commit, `at` and the link;
- Promote moves the row ready → building (in about 1 s) → ready, and it is no longer behind;
- with the publish script broken, the row shows `failed` with the message, and the old instance kept answering HTTP 200.

Not done here: the triferto-forecast check from the cmd's Verify. It needs `install.ps1`, a Windows machine and Tailscale. The Windows code paths are untested by hand: lock semantics with `FileShare.None`, spawning a `.cmd` publish wrapper directly, and the rename retry.

## What changed

**mf-run**
- `Config/RunConfig.cs`:
  - parses the optional `run.stable` into `StableConfig` (`Publish`, `Command`, `Args`, `Url`, `Link`, `Timeout` with a default of 15 min);
  - validation errors: no `publish`, no `command`, or `timeout_minutes` ≤ 0;
  - a service named `stable` is rejected as reserved;
  - `StableConfig.ToService(root)` makes stable an ordinary `ServiceConfig` named `stable`. Command and workdir are under `.magnaflow/stable/current/`. `ServiceManager` and `PidFile` are reused unchanged, so PID and log are `.magnaflow/run/stable.{pid,log}`.
- `Infrastructure/IProcessSpawner.cs`, `SystemProcessSpawner.cs`:
  - new `RunAsync(exe, args, workdir, logPath?, timeout?) → CommandResult`, a run-and-wait on the existing seam;
  - output goes to a log file (truncated) or is returned;
  - on a timeout the whole tree is killed, and draining the output is bounded to 10 s.
- New runtime files:
  - `Runtime/StableState.cs`: `state.yml`, read and written through YamlDotNet, nulls omitted.
  - `Runtime/PromoteLock.cs`: `FileShare.None` lock, `TryAcquire`, plus an `IsHeld` probe for `status`.
  - `Runtime/StableInstance.cs`: `PromoteAsync` (the spec's steps 1–6, exit 0/1/3) and `StatusAsync`, the stable entry with the additive fields. A `building` while the lock is free reads as `failed` / "promote interrupted". Status only reads; it never rewrites the file.
  - `Runtime/RunTargets.cs`: target selection. No name means services only; `stable` means the stable instance.
- `Runtime/ServiceManager.cs`: `ServiceStatusEntry` gained nullable `Stable`, `State`, `Sha`, `Dirty`, `At`, `Message`, `Link`. They are omitted from JSON when null, so service entries serialize exactly as before (tested).
- `Program.cs`:
  - new verb `promote`;
  - `status` appends the stable entry;
  - the text line gets ` — <state> <sha7> (dirty) at <at>: <message>`;
  - usage text updated.
- README: usage and exit code 3.
- Tests:
  - `RunConfigTests`: stable parsed, defaults, invalid blocks, service `stable` rejected, old configs give `Stable == null`.
  - New `StableInstanceTests`:
    - happy path;
    - `building` is written during the publish;
    - first promote without a `prev/`;
    - publish arguments, workdir, log and timeout;
    - publish fails;
    - publish times out;
    - publish output has no command;
    - the new process dies, then the old build is restored and started;
    - lock held → 3, nothing changed;
    - lock freed afterwards;
    - stale `building` → `failed`, and `building` with the lock held stays `building`;
    - JSON shape;
    - verbs without a name leave stable alone;
    - target selection by name.

**mf-worker**
- `Config/ProjectConfig.cs`: `HasRunStable = run.stable present`, read like `HasRunServices`.
- `Execution/TaskRunner.cs`: after the terminal commit and push and the final "done" line, `if (isDone && cmd.Branch is null && config.HasRunStable)` it runs `<run.command> promote --project <root>` through the existing `RunMfRunAsync`, with no timeout. It prints one line, `[id] <run.command> promote: exit N`. The return value is untouched.
- Tests:
  - `TaskRunnerTests`:
    - promote comes after the push, and no git operation follows it;
    - order is stop/start/promote;
    - a failing promote leaves exit, status and rst unchanged;
    - a spawn failure leaves the exit code unchanged;
    - no promote after aborted or questions, for a `branch:` cmd, or without `run.stable`.
  - `ProjectConfigTests`: `HasRunStable` detection.
  - `FakeProcessRunner` gained an `OnExecutable` hook.

**mf-cockpit**
- `Config/ProjectConfigReader.HasRunStable`.
- `Models/Dtos.cs`:
  - `RunServiceStatusDto` gained the stable fields;
  - `RunStatusDto.Stable` holds the split-off entry;
  - every new field is `[JsonIgnore(WhenWritingNull)]`, so the API response for a project without stable is byte-identical.
- `Api/RunEndpoints.cs`:
  - the status read splits the stable entry off;
  - Start/Stop/Restart accept `stable` when configured;
  - new `POST /api/projects/{name}/run/stable/promote`:
    - 404 without `run.stable`;
    - 409 while a lane command is `running`;
    - 409 while a fresh (uncached) mf-run status says `building`;
    - otherwise a detached spawn and 202 with a `RunActionResponseDto`.
- `Infrastructure/IRunClient.StartPromote` and `MfRunClient`: spawn detached through the existing `IWatchProcessSpawner`. No wait, so `run.timeout_seconds` does not apply.
- `Live/ProjectWatcher.cs`, `ProjectWatchersHostedService.cs`:
  - a change to `.magnaflow/stable/state.yml` drops the project's cached run status and raises a `run` event;
  - everything else under `.magnaflow/stable/` (thousands of publish files) raises no event. Before this change it would have raised `evidence`.
- `wwwroot/project.html`:
  - the card shows when there are services or stable, and Start all / Stop all are hidden when there are no services;
  - the stable row has the process state, a state pill, sha7 + dirty, **behind** (sha ≠ `commits[0].hash` from the Git card's info), `at`, and the message on failure;
  - the link is `link` verbatim, else `displayUrl(url)`;
  - buttons Log/Start/Stop/Restart/Promote. Promote is disabled with a `title` while `building` or while a lane command runs;
  - the row re-renders when the git info or the lane loads, but only when a stable row exists.
- Tests:
  - `RunApiIntegrationTests`:
    - stable split off; no stable entry without `run.stable`;
    - Promote 202 + spawn, 409 running, 409 building, 404 without stable;
    - `stable` by name accepted or 404.
  - `RunE2ETests.Promote_spawns_mf_run_detached_and_returns_at_once`: a real stub, generated as `.sh` on Unix or `.cmd` on Windows. `status` answers at once, `promote` sleeps 5 s. The POST must return in under 4 s, and the stub must still finish afterwards. The test runs on Linux too, unlike the other E2E tests.

**Specs**
- `run/mf-run.md`:
  - the exit code of `status` with stable;
  - verbs on a stable-only project;
  - `failed` keeps the previous sha;
  - git failure is not fatal;
  - a failed stop aborts before the swap;
  - restore moves the new build back to `next/`;
  - the lock file is kept.
- `cockpit/project.md#run`:
  - the card shows for `run.stable` too, and Start/Stop all are hidden without services;
  - no change for projects without stable;
  - in "Live updates", the cache invalidation on `state.yml` and that publish files raise no event.
- `concepts/evidence-layout.md`: `run/stable.{pid,log}`, `stable-publish.log` and `stable/` (next, current, prev, `state.yml`, `promote.lock`) in the tree, one "why machine-local" bullet, and Code/Why lines.

## Deviations from the specs, and why

- **Cockpit cache invalidation on `state.yml`.** The spec said "the row follows `building` through the normal refetch". In the live test that was not true. The status read is cached for 3 s per project. The read right after the Promote click cached `ready` before mf-run wrote `building`. At the end, the swap's pid and log events cached `building` just before `ready` was written. The row stayed on `building` until some unrelated event. Dropping the cache on every `state.yml` change fixes both, and it is only three writes per promote. The spec now says this.
- **Card visibility.** The spec said "Shown only when the project has `run:` services". A stable-only project would never see its row, so the card now also shows for `run.stable`, and Start/Stop all are hidden there.
- **Restore message.** The spec quoted one message for both failure kinds. A failed rename now says "could not swap in the new build (…); previous restored". The spec now says "with the cause, e.g. …".

## Self-answered questions

- **"The existing process seam" for git in mf-run.** mf-run only had the detached `IProcessSpawner`. I added a run-and-wait `RunAsync` to that same interface instead of a second seam or CliWrap. It is plain `System.Diagnostics`, with no new dependency.
- **Exit code of `status` without a name when stable is configured.** It stays the services' own, because "all never includes stable". `status stable` exits by stable alone. This is in the spec.
- **What `sha`/`dirty` a `failed` state holds.** The previous state's values, meaning the build that still runs. With the attempted sha, Behind would compare HEAD against a build that never ran. `building` shows the new sha while it builds. One edge case: after an interrupted promote, `state.yml` still holds the attempted sha, and `status` shows it as `failed`.
- **git unavailable during promote.** Not fatal. The publish goes ahead, and `sha`/`dirty` are left out.
- **Old process cannot be stopped.** The promote fails before any rename, and `current/` stays where it is.
- **The new build after a failed start.** It is moved back to `next/`, where it can be inspected and where the next promote empties it. It is not deleted.
- **First promote (no `current/`).** It swaps without a `prev/`. If the new build then dies, the message is "new build did not start", with nothing restored.
- **Lock file lifetime.** Never deleted. Deleting it would let two promotes lock different inodes on Unix. The cmd says `FileShare.None` on Windows, while mf-watch uses `FileShare.Read` there. I followed the cmd.
- **Verbs on a project with only `run.stable`.** start/stop/restart without a name print `no services configured` and exit 0, as before. `status` lists stable alone. `start stable` and the rest work.
- **The cockpit's detached spawn seam.** I reused `IWatchProcessSpawner` (spawn, PID, no wait) instead of adding a third spawner. Its output is inherited by the cockpit's console. The outcome is read back through `state.yml`.
- **Promote response code.** 202 Accepted with the usual `RunActionResponseDto`. A spawn failure is 200 with `success: false`, like the other run actions report mf-run failures.
- **"Building" check in the endpoint.** It uses a fresh, uncached mf-run status, so a cached `ready` cannot let a double click through. mf-run's lock (exit 3) is the real guard anyway.
- **The worker's stdout line.** One line after promote finishes, through the runner's normal `info` sink: `[<id>] mf-run promote: exit N`. Promote's own output is not streamed into mf-watch's log. It is in `.magnaflow/run/stable-publish.log` and `state.yml`.
- **Where Behind gets HEAD.** From the Git card's info, `commits[0].hash`, which is `git log` of HEAD. No new git call.

## Uncertain / for the next session

- Windows by hand, as in the Verify list: the lock, a `.cmd` publish wrapper started directly (`CreateProcess` handles `.cmd` and `.bat`), and the rename retry against handles held after a kill.
- `dotnet publish` under a redirected run: a build-server process that escapes the tree and keeps the pipe open could keep a publish "running" until the timeout. On a timeout the drain is bounded, but a hang before the timeout would show as a slow publish. Watch for this in triferto-forecast.
- On a narrow window the stable row's buttons wrap onto a second line. That is fine, and left as is.
