---
title: "install: publish before stopping, opt-in self-update on Linux, no stale cockpit assets"
cmd: 0024-cmd-install-self-update.md
done: 2026-10-06
summary: "install.sh/install.ps1 now publish into a staging dir before stopping anything, install.sh --self-update adds a 2-minute timer that re-installs new tools/ commits once nothing is running, and the cockpit sends Cache-Control: no-cache on all static files. To use it, run tools/install/install.sh --self-update once by hand; it was not run for real here."
---

# 0024 install self-update: report

## What changed

- **`tools/install/install.sh`**:
  - **Publish first.** Step 1 publishes all four tools into `<install-dir>/.staging/<tool>/` while the daemons run. A leftover `.staging` is removed first. A failed `dotnet publish` removes `.staging`, prints `error: dotnet publish failed for <tool> — nothing was stopped, …` and exits 1, before any stop.
  - **Then stop and swap.** Step 2 stops the daemons (unchanged logic), then for each tool does `rm -rf <tool>` and `mv .staging/<tool> <tool>`.
  - The legacy `mf-cockpit.yml` is now removed from the staged copy.
  - The steps after that are unchanged.
  - **Recorded commit.** At the end it writes `<install-dir>/installed-commit`, the `HEAD` read at the *start* of the run.
  - **New `--self-update` flag**:
    - copies `mf-selfupdate.sh` into the install dir;
    - writes `mf-selfupdate.service` (oneshot, `TimeoutStartSec=15min`, same `PATH`/`SSH_AUTH_SOCK` as the other units, `ExecStart` carries `--repo/--install-dir/--config`);
    - writes `mf-selfupdate.timer` (`OnBootSec=2min`, `OnUnitInactiveSec=2min`);
    - runs `enable --now` on the timer.

    `--config` is made absolute so the unit can reuse it.
- **`tools/install/mf-selfupdate.sh`** (new): one decision per tick, in the order given in the spec (`docs/specs/concepts/machine-install.md#self-update`). It logs to stdout (the journal) and to `<install-dir>/selfupdate.log`, capped at 1000 lines. It runs the repo's `install.sh --self-update` with the same install dir and config.
- **`tools/install/install.ps1`**: same publish-first flow (`$InstallDir\.staging\<tool>`, `exit 1` on failure after removing staging). The stop block, including `Get-WatchedProjects`, moved after the publish, followed by `Remove-Item` + `Move-Item` per tool. The restart logic is unchanged.
- **Cockpit** (`Program.cs`): `UseStaticFiles` with `OnPrepareResponse` sets `Cache-Control: no-cache`. `UseDefaultFiles` rewrites `/` first, so `/` is covered too. New test `StaticFilesCacheTests` checks `/`, `/index.html`, `/specs.html`, `/assets/app.js` and `/assets/style.css` for 200, `no-cache` and an `ETag`.
- **Specs and docs**:
  - `concepts/machine-install.md`: publish-first, the installed-commit row, `--self-update`, a new `#self-update` section, the dogfooding section, and the `Code:` line.
  - `cockpit/index.md`: new `## Serving` section.
  - `_overview.md` ("This repo": rollout is no longer only manual).
  - `tools/install/README.md`.
  - `spec_lint`: 0 problems.

## Decisions taken while implementing

- **Installed commit = `HEAD` read before publishing.** If a commit lands during the install, it still counts as new on the next tick, instead of being marked installed without having been built.
- **Every self-update tick exits 0**, including `failed:`. The verdict is in the log/journal. This keeps the timer re-arming without depending on how `OnUnitInactiveSec` treats a unit in `failed` state.
- **Failed installs are sticky per commit.** `selfupdate-failed-commit` holds the `HEAD` that failed; ticks skip while `HEAD` equals it, and a success deletes it. The "already failed" check comes *before* the running-cmd check, so the log says the more useful reason.
- **Self-update re-runs `install.sh --self-update`**, so an automatic install also refreshes the installed `mf-selfupdate.sh` and its units. A plain `install.sh` without the flag never touches or removes an existing timer.
- **Watched projects = running `mf-watch@*` units**, unescaped with `systemd-escape --unescape` (what `%I` does). A stopped watcher can't have a live worker, so its project isn't checked. The environment variable `MF_SELFUPDATE_PROJECTS` replaces this lookup; it exists for tests only.
- **Frontmatter check**: only the first `---`…`---` block counts, CRLF tolerated. A `status: running` line in the body does not count (tested).

## Verification (nothing touched the live install)

### `install.sh --dry-run` (on this machine, read-only queries only)

```text
== MagnaFlow install (repo: /home/paul/Documents/git/magnaflow -> /home/paul/tools/magnaflow) ==
-- publishing mf-run -> /home/paul/tools/magnaflow/.staging/mf-run
+ dotnet publish /home/paul/Documents/git/magnaflow/tools/mf-run/src/MagnaFlow.MfRun/MagnaFlow.MfRun.csproj -c Release -o /home/paul/tools/magnaflow/.staging/mf-run -p:AssemblyName=mf-run --nologo
-- publishing mf-worker -> /home/paul/tools/magnaflow/.staging/mf-worker
+ dotnet publish /home/paul/Documents/git/magnaflow/tools/worker-controller/src/MagnaFlow.WorkerController/MagnaFlow.WorkerController.csproj -c Release -o /home/paul/tools/magnaflow/.staging/mf-worker -p:AssemblyName=mf-worker --nologo
-- publishing mf-watch -> /home/paul/tools/magnaflow/.staging/mf-watch
+ dotnet publish /home/paul/Documents/git/magnaflow/tools/mf-watch/src/MagnaFlow.MfWatch/MagnaFlow.MfWatch.csproj -c Release -o /home/paul/tools/magnaflow/.staging/mf-watch -p:AssemblyName=mf-watch --nologo
-- publishing mf-cockpit -> /home/paul/tools/magnaflow/.staging/mf-cockpit
+ dotnet publish /home/paul/Documents/git/magnaflow/tools/mf-cockpit/src/MagnaFlow.MfCockpit/MagnaFlow.MfCockpit.csproj -c Release -o /home/paul/tools/magnaflow/.staging/mf-cockpit -p:AssemblyName=mf-cockpit --nologo
+ rm -f /home/paul/tools/magnaflow/.staging/mf-cockpit/mf-cockpit.yml
-- stopping running MagnaFlow daemons (restarted after the install)
+ systemctl --user stop mf-cockpit.service
+ systemctl --user stop mf-watch@-home-paul-Documents-git-klaas-.service
+ systemctl --user stop mf-watch@-home-paul-Documents-git-magnaflow-.service
+ systemctl --user stop mf-watch@-home-paul-Documents-git-pistart.service
+ systemctl --user stop mf-watch@-home-paul-Documents-git-wozzol.service
+ systemctl --user stop mf-watch@-home-paul-nxtphase-triferto\x2dforecast.service
+ pkill -x mf-cockpit
+ pkill -x mf-watch
+ rm -rf /home/paul/tools/magnaflow/mf-run
+ mv /home/paul/tools/magnaflow/.staging/mf-run /home/paul/tools/magnaflow/mf-run
+ rm -rf /home/paul/tools/magnaflow/mf-worker
+ mv /home/paul/tools/magnaflow/.staging/mf-worker /home/paul/tools/magnaflow/mf-worker
+ rm -rf /home/paul/tools/magnaflow/mf-watch
+ mv /home/paul/tools/magnaflow/.staging/mf-watch /home/paul/tools/magnaflow/mf-watch
+ rm -rf /home/paul/tools/magnaflow/mf-cockpit
+ mv /home/paul/tools/magnaflow/.staging/mf-cockpit /home/paul/tools/magnaflow/mf-cockpit
+ rm -rf /home/paul/tools/magnaflow/.staging
-- installed into /home/paul/tools/magnaflow: mf-run mf-worker mf-watch mf-cockpit
+ mkdir -p /home/paul/.local/bin
+ ln -sf /home/paul/tools/magnaflow/mf-run/mf-run /home/paul/.local/bin/mf-run
+ ln -sf /home/paul/tools/magnaflow/mf-worker/mf-worker /home/paul/.local/bin/mf-worker
+ ln -sf /home/paul/tools/magnaflow/mf-watch/mf-watch /home/paul/.local/bin/mf-watch
+ ln -sf /home/paul/tools/magnaflow/mf-cockpit/mf-cockpit /home/paul/.local/bin/mf-cockpit
-- symlinked into /home/paul/.local/bin: mf-run mf-worker mf-watch mf-cockpit
+ mkdir -p /home/paul/.config/magnaflow
-- machine config already exists, leaving it as-is: /home/paul/.config/magnaflow/magnaflow.yml
+ mkdir -p /home/paul/.config/systemd/user
+ write /home/paul/.config/systemd/user/mf-cockpit.service
+ write /home/paul/.config/systemd/user/mf-watch@.service
-- systemd units: /home/paul/.config/systemd/user/mf-cockpit.service, /home/paul/.config/systemd/user/mf-watch@.service
+ systemctl --user daemon-reload
+ systemctl --user enable mf-cockpit.service

+ systemctl --user restart mf-cockpit.service
+ systemctl --user restart mf-watch@-home-paul-Documents-git-klaas-.service
+ systemctl --user restart mf-watch@-home-paul-Documents-git-magnaflow-.service
+ systemctl --user restart mf-watch@-home-paul-Documents-git-pistart.service
+ systemctl --user restart mf-watch@-home-paul-Documents-git-wozzol.service
+ systemctl --user restart mf-watch@-home-paul-nxtphase-triferto\x2dforecast.service
+ record installed commit 436df4bcb2e699ceb00e743cd2cfb05b0aa16af3 -> /home/paul/tools/magnaflow/installed-commit
== Done. Cockpit + watcher restarted on the new version.
-- headless machine? run: loginctl enable-linger $USER   (user services otherwise stop at logout)
rc=0
```

### `install.sh --self-update --dry-run`

Identical to the above, plus these lines (diff):

```text
41a42,45
> + install -m 755 /home/paul/Documents/git/magnaflow/tools/install/mf-selfupdate.sh /home/paul/tools/magnaflow/mf-selfupdate.sh
> + write /home/paul/.config/systemd/user/mf-selfupdate.service
> + write /home/paul/.config/systemd/user/mf-selfupdate.timer
> -- self-update: /home/paul/.config/systemd/user/mf-selfupdate.service, /home/paul/.config/systemd/user/mf-selfupdate.timer (log: /home/paul/tools/magnaflow/selfupdate.log)
43a48
> + systemctl --user enable --now mf-selfupdate.timer
```

### Publish-first with a broken build

Setup:
- a scratch copy of the working tree (`rsync`, no `.git`/`bin`/`obj`) in `/tmp/mf0024/repo`;
- a scratch install dir `/tmp/mf0024/install`, pre-seeded with four fake tool folders, with their sha256 sums recorded;
- `HOME=/tmp/mf0024/home`;
- `systemctl`/`pkill`/`pgrep` replaced by logging stubs on `PATH`, so even an unexpected success could not stop a live unit;
- a syntax error appended to the scratch `tools/mf-watch/.../Program.cs`, so the third publish fails after two have succeeded.

```text
rc=1
-- publishing mf-run -> /tmp/mf0024/install/.staging/mf-run
-- publishing mf-worker -> /tmp/mf0024/install/.staging/mf-worker
-- publishing mf-watch -> /tmp/mf0024/install/.staging/mf-watch
…/Program.cs(110,1): error CS1002: ; expected
error: dotnet publish failed for mf-watch — nothing was stopped, the installed tools in /tmp/mf0024/install are untouched
--- stub calls:            (none: no systemctl/pkill call at all, so nothing was stopped)
--- install dir after:     mf-cockpit mf-run mf-watch mf-worker   (.staging removed)
scratch install unchanged  (sha256 of every file identical before/after)
```

Then the same scratch run with the syntax error reverted, plus `--self-update`, still with the stubs. Result: `rc=0`.
- **Stub call order**: `status`, `is-active`, `list-units`, `stop mf-cockpit.service`, `pkill …`, `daemon-reload`, `enable mf-cockpit.service`, `enable --now mf-selfupdate.timer`, `restart mf-cockpit.service`. So stop comes after all four publishes, and restart comes last.
- **Install dir**: the tool folders now contain real ELF binaries, and `mf-selfupdate.sh` was copied in.
- **Generated units** are as described above.
- **No `installed-commit`**: the scratch copy has no `.git`, so the script printed its "not a git checkout" warning instead.

### Self-update decisions

Setup: a scratch git repo with a stub `tools/install/install.sh` (it logs its argv, fails when `/tmp/…/fail` exists, and otherwise writes `installed-commit`). Two fake projects were passed through `MF_SELFUPDATE_PROJECTS`; `projB`'s `0001` has `status: running` in its *body* only.

```text
## 0. no installed-commit                                   -> skipped: no installed-commit in /tmp/mf0024-su/install (run install.sh once)
## 1. no new commit                                         -> skipped: no new commit (f6ea0c7)
## 2. docs-only commit                                      -> skipped: no tools/ change in f6ea0c7..a24c1fc
## 3a. tools/ commit, running cmd in a watched project      -> skipped: /tmp/mf0024-su/projB/docs/prompts/0002-cmd-c.md is running
## 3b. tools/ commit, running cmd (CRLF) in the repo itself -> skipped: /tmp/mf0024-su/repo/docs/prompts/0009-cmd-self.md is running
## 4a. nothing running, install fails                       -> failed: install.sh exited 3 for f6ea0c7..75fe07b (see journalctl --user -u mf-selfupdate)
## 4b. same HEAD after the failure                          -> skipped: install of 75fe07b failed, waiting for a new commit
## 4c. same HEAD, stub fixed                                -> skipped: install of 75fe07b failed, waiting for a new commit
## 5. new tools/ commit, nothing running                    -> installed: f6ea0c7..99a11be
## 6. next tick                                             -> skipped: no new commit (99a11be)
stub install.sh called twice: --self-update --install-dir /tmp/mf0024-su/install --config /dev/null
```

The same lines landed in `selfupdate.log`, and every tick exited 0. Separately, a read-only check of the real lookup: the five running `mf-watch@` instances on this machine unescape to their project paths, and each has a `docs/prompts`.

### `install.ps1`

Not run and not parsed: `pwsh` is not installed on this machine. The change was reviewed by reading only. The publish loop now targets `.staging`, and the stop block, including `Get-WatchedProjects`, moved unchanged below it. Remove/Move per tool, the restart is unchanged. The first real run on Windows is its test.

### Tests

`dotnet test tools/mf-cockpit/MfCockpit.slnx`: 316 passed. The 5 new `StaticFilesCacheTests` cases are among them.

## Not done / uncertain

- **Nothing was installed for real, and no live unit was touched.** Enabling self-update on this machine is a manual `tools/install/install.sh --self-update`. It also installs the current build, so it stops and restarts the watchers: run it when no command is running.
- **A stale `status: running`** (left by a killed worker) blocks self-update until a human resets it. The log names the file every tick. This is intended.
- **Unescaped paths in `ExecStart`.** Repo or install paths containing spaces would break `ExecStart` in all generated units. That was already true before this change; the self-update unit doesn't make it worse.
- **No lock between a manual `install.sh` and a timer tick that fires during it.** Overlap is unlikely: the timer re-arms only after its own run ends, and a manual run takes about a minute. Not guarded.
- **Windows: an orphaned `mf-worker.exe` from a killed watcher** can still lock `<InstallDir>\mf-worker\`. This is as before, but it now fails at the `Remove-Item` swap step, *after* the daemons were stopped. Out of scope (no Windows self-update).

## Self-answered questions

- **Where does the self-update script get repo root and config?** From its own `ExecStart` arguments, written by `install.sh --self-update`. Only the commit is read from a plain file (`installed-commit`).
- **Which `HEAD`?** The checked-out `HEAD` of the repo, local only, never fetched. `git_sync` in the watcher does the pulling.
- **Which commit is "installed"?** The `HEAD` read at the start of `install.sh`, not at the end (see decisions).
- **What does "retried only after a new commit" mean concretely?** A `selfupdate-failed-commit` file holding the failed `HEAD`, cleared on success.
- **Does an automatic install keep the timer?** Yes: it runs `install.sh --self-update`. A manual run without the flag leaves an existing timer alone ("without the flag nothing changes").
- **Which projects count as "watched"?** The *running* `mf-watch@` instances, plus the repo itself, not the cockpit's project list.
- **Log growth from a skip line every 2 minutes**: the log file is capped at its last 1000 lines. The journal keeps everything.
- **Exit code of a tick**: always 0 (see decisions).
- **Which cockpit spec gets the cache rule?** `cockpit/index.md` (new `## Serving`). There is no cockpit `_overview.md`. `docs/specs/_overview.md` got a one-line update in "This repo" because it still said rollout stays manual.
- **`CLAUDE.md` dogfooding note** ("install.ps1, by hand … never in a cmd's build/test"): left unchanged. It is still true that a cmd never installs.
