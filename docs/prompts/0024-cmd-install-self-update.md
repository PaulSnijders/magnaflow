---
title: "install: publish before stopping, opt-in self-update on Linux, no stale cockpit assets"
status: ready
created: 2026-10-06
---

## Context

MagnaFlow now develops itself: a worker on the Linux machine finishes a
cmd that changes `tools/`, and a human then runs `tools/install/install.sh`
by hand to roll it out. The goal is to make that rollout automatic, safely.
Three things stand in the way:

- **`install.sh` stops the daemons before it publishes** (step 0, then
  step 1). If `dotnet publish` fails, the script exits with the cockpit
  and every watcher down. By hand you notice; unattended, MagnaFlow would
  silently stop.
- **Stopping `mf-watch@` units kills an in-flight worker**
  (`systemctl --user stop` ends the unit's whole cgroup), which leaves its
  command at `running`. So an install may only happen when no command is
  running, and it must not run inside a watcher's process tree.
- **The cockpit sends no `Cache-Control`.** Static files get only `ETag`
  and `Last-Modified`, so after an install Chrome kept serving the old
  `app.js` from its heuristic cache next to the new `style.css` (seen live
  on 2026-10-06: `--summary-bar-h` stayed 0 until a hard reload).

Read `docs/specs/concepts/machine-install.md` and
`docs/specs/concepts/watch-supervision.md` first.

## Task

1. **Publish before stopping**, in `install.sh` and `install.ps1`.
   - Publish all four tools into a staging directory inside the install
     dir (for example `<install-dir>/.staging/<tool>/`) while the daemons
     keep running.
   - Only when every publish succeeded: stop the daemons (as today),
     replace each tool folder with its staged copy, and continue with the
     existing steps and the restart.
   - On a failed publish: leave the running daemons and the installed
     tools untouched, remove the staging dir, print what failed, exit
     non-zero.
   - Keep `--dry-run` working, and keep the Windows watcher-restart logic
     in `install.ps1` as it is.
2. **Opt-in self-update on Linux**: `install.sh --self-update` additionally
   installs a `systemd --user` timer + service (for example
   `mf-selfupdate.timer`, every 2 minutes) that runs a small script. Without
   the flag nothing changes. The script:
   - reads the repo root it was installed from and the last installed
     commit (a plain file in the install dir, written by every successful
     `install.sh` run, manual or automatic);
   - does nothing unless `HEAD` differs from that commit **and**
     `git diff --name-only <installed>..HEAD -- tools/` is non-empty
     (docs-only commits never trigger an install);
   - does nothing while any command is `running`: check the
     `docs/prompts/*-cmd-*.md` frontmatter of every watched project (the
     `mf-watch@` instances' paths) and of the repo itself;
   - otherwise runs `install.sh` (same install dir and config as the
     original install) and logs one line per decision (skipped: why /
     installed: old..new / failed: why) to the journal and to a log file
     in the install dir.
   - It runs as its own unit, never inside a watcher's cgroup, so stopping
     the watchers does not stop it. A failed install leaves everything
     running (step 1) and is retried on the next tick only after a new
     commit, not every 2 minutes.
3. **Cockpit: no stale assets.** Serve every static file (`wwwroot/`,
   including the `.html` pages) with `Cache-Control: no-cache`, so the
   browser revalidates with the existing `ETag` on every load. Add an
   xUnit test that a static asset response carries the header.
4. **Verify — never against the live install.** This run executes on the
   very machine whose watcher runs it: do **not** run `install.sh` for
   real, do not touch the real `systemd --user` units, and do not restart
   the cockpit or watchers.
   - `install.sh --dry-run` and `install.sh --self-update --dry-run`:
     show the printed plan in the rst.
   - Test the publish-first path with `--install-dir` pointing at a
     scratch directory and a deliberately broken build (for example a
     temporary syntax error in a scratch copy of the repo): show that the
     scratch install is left untouched.
   - Test the self-update script's decisions against a scratch git repo
     with fake projects: no new commit → skip; docs-only commit → skip;
     `tools/` commit with a `running` cmd → skip; `tools/` commit with
     nothing running → would install (stub `install.sh`).
   - `install.ps1` cannot run here: say so, and check it with
     `pwsh -NoProfile -Command` parse-only if `pwsh` is available.
5. **Spec-first:** update `docs/specs/concepts/machine-install.md`
   (publish-first, the self-update timer, its guards and log) and the
   cockpit spec that covers serving pages (add the cache rule; `index.md`
   or `_overview.md`, your call). `tools/install/README.md` gets the
   `--self-update` flag.

## Not in scope

- Self-update on Windows (the desktop is updated by hand).
- Rolling back a bad but successfully built release.
- Any change to mf-watch or mf-worker: the timer is a separate actor.
