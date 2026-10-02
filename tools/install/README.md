# MagnaFlow machine install

One-time setup of the MagnaFlow tools on a Windows machine (re-runnable — also
how you roll out tool updates and config changes).

```powershell
powershell -ExecutionPolicy Bypass -File C:\GIT\magnaflow\tools\install\install.ps1
```

What it does: publishes **mf-run / mf-worker / mf-watch / mf-cockpit** (Release,
renamed to those short exe names) to `C:\Tools\MagnaFlow\<tool>\`, **seeds**
the machine config into `%APPDATA%\MagnaFlow\magnaflow.yml` (the one place
every tool looks) only if that file doesn't exist yet — once it's there it's
your live config (projects added via the cockpit, hand edits, etc.) and a
re-run never touches it — puts the tool folders on the user PATH, and creates
a **MagnaFlow** shortcut (desktop + Start menu, with the square MagnaFlow
logo) that runs `start-magnaflow.ps1`: cockpit + watcher, each minimized in
its own console window, then opens <http://localhost:5210>.

**Updating = the same one command.** Tool code changed (e.g. a MagnaFlow cmd
just landed)? Just re-run the script: it stops running daemons first (a
running exe is locked, and daemons keep old code in memory anyway),
re-publishes, and restarts everything on the new version — your
`magnaflow.yml` is left alone. To pick up a machine-config change, edit
`%APPDATA%\MagnaFlow\magnaflow.yml` directly (or delete it and re-run to
re-seed from the repo copy).

Different install dir / config source / watched project: see the `param(...)`
blocks of `install.ps1` and `start-magnaflow.ps1`.

Requires the .NET 10 SDK. Stopping a daemon = close its console window (restore
it from the taskbar first).

## Linux (worker machine)

One-time setup — also the update path, same as Windows:

```bash
tools/install/install.sh --config tools/install/magnaflow.linux.yml
```

What it does: publishes **mf-run / mf-worker / mf-watch / mf-cockpit**
(Release, renamed to those short names) to `~/tools/magnaflow/<tool>/`,
symlinks them into `~/.local/bin/` (warns if that's not on `PATH`), **seeds**
the machine config into `~/.config/magnaflow/magnaflow.yml` from the given
`--config` only if that file doesn't exist yet — once it's there it's your
live config and a re-run never touches it — and generates + installs two
`systemd --user` unit files:

- `mf-cockpit.service` — the dashboard + chat daemon, `Restart=on-failure`,
  enabled so it starts at login.
- `mf-watch@.service` — a *template* unit, one instance per watched project.
  Enable one per project you want watched (`%i`/`%I` become the project path):
  ```bash
  systemctl --user enable --now mf-watch@$(systemd-escape /path/to/project).service
  ```
  `Restart=no` — mf-watch's exit codes are meaningful (it isn't a crash loop
  to paper over).

**Updating = the same one command.** Re-running `install.sh` stops whatever
was running (`systemctl --user stop`, falling back to `pkill` on exact process
names for anything started outside systemd), re-publishes, and restarts
exactly what was running before — cockpit and/or any watch instances — on the
new version. Your `magnaflow.yml` is left alone; edit it directly (or delete
it and re-run to re-seed from `--config`) to pick up a machine-config change.

**Headless machine**: user services stop at logout unless lingering is
enabled — `install.sh` reminds you at the end:

```bash
loginctl enable-linger "$USER"
```

**Logs**: `systemd --user` captures stdout/stderr in the journal —

```bash
journalctl --user -u mf-cockpit -f
journalctl --user -u mf-watch@<escaped-instance> -f
```

**Dry run**: `install.sh --dry-run` prints every action (publish, symlink,
config sync, unit generation, service start/stop) without doing any of it —
safe to sanity-check on a machine without the .NET SDK, or on Windows via Git
Bash/WSL.

**mf-run stays out of scope of systemd/PM2.** These units supervise
MagnaFlow's own daemons only — never the *target project's* dev/debug
instance, whose crashes must stay visible to the worker (that's what
`mf-run`, driven by `mf-worker`, is for). Supervising the target app under
systemd too would hide the very failures the worker needs to see and act on.

Different install dir / config source: `install.sh --help`.

Requires the .NET 10 SDK and a user systemd instance (`systemctl --user`). No
sudo anywhere — user-level install only.
