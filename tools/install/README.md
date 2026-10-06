# MagnaFlow machine install

## TL;DR

```powershell
# Windows
powershell -ExecutionPolicy Bypass -File C:\GIT\magnaflow\tools\install\install.ps1
```

```bash
# Linux
tools/install/install.sh
```

Same command for the first install and every update: it publishes the tools
first, and only when that succeeded stops the running ones, swaps in the new
build and starts them again. A failed build changes nothing. Your machine config
(`magnaflow.yml`) is never overwritten. Requires the .NET 10 SDK.

## What you get

| | Windows | Linux |
|---|---|---|
| Tools (mf-run, mf-worker, mf-watch, mf-cockpit) | `C:\Tools\MagnaFlow\<tool>\`, on the user PATH | `~/tools/magnaflow/<tool>/`, symlinked into `~/.local/bin/` |
| Machine config (seeded once) | `%APPDATA%\MagnaFlow\magnaflow.yml` | `~/.config/magnaflow/magnaflow.yml` |
| How it runs | **MagnaFlow** shortcut (desktop + Start menu): cockpit + watcher in minimized consoles, opens <http://localhost:5210> | `systemd --user` units: `mf-cockpit.service` + one `mf-watch@<project>` per watched project |

To change the machine config: edit `magnaflow.yml` directly (or delete it and
re-run to re-seed it).

## First install on a new machine

**Windows** — the defaults point at Paul's setup (config seeded from
`C:\GIT\wozzol2\.magnaflow\magnaflow.yml`, watcher on `C:\GIT\wozzol2`).
Elsewhere, pass your own config source with `-MachineConfig <path>`, and set
the watched project via `start-magnaflow.ps1 -Project <path>` (see the
`param(...)` blocks of both scripts). To stop a tool, close its console
window (restore it from the taskbar first).

**Linux** — the config is seeded from `tools/install/magnaflow.linux.yml`
(override with `--config <path>`). Then:

```bash
# watch a project (once per project; %i becomes the path)
systemctl --user enable --now mf-watch@$(systemd-escape /path/to/project).service

# headless machine: keep the services running after logout
loginctl enable-linger "$USER"
```

No sudo anywhere — user-level install only.

## Linux reference

- **Logs**: `journalctl --user -u mf-cockpit -f` /
  `journalctl --user -u mf-watch@<escaped-instance> -f`
- **Self-update** (opt-in): `install.sh --self-update` also installs
  `mf-selfupdate.timer`. Every 5 minutes it re-runs the install when the
  repo's `HEAD` has a new commit that changed `tools/` and no command is
  `running` in any watched project (or this repo). It never fetches; the
  watcher's `git_sync` pulls. A failed install is retried only after the next
  commit. One line per decision in `~/tools/magnaflow/selfupdate.log` and in
  `journalctl --user -u mf-selfupdate`. Off again:
  `systemctl --user disable --now mf-selfupdate.timer`.
- **Dry run**: `install.sh --dry-run` prints every action without doing it
  (works without the .NET SDK, or on Windows via Git Bash/WSL).
- **Other options**: `install.sh --help`.
- **Update** restarts exactly what was running before (cockpit and/or watch
  instances); processes started outside systemd are stopped with `pkill`.
- `mf-watch@` has `Restart=no` on purpose: its exit codes mean something.
- **systemd supervises MagnaFlow's own daemons only** — never the target
  project's dev/debug process. That one is `mf-run`'s job, driven by the
  worker, so its crashes stay visible to the worker instead of being
  restarted away.
