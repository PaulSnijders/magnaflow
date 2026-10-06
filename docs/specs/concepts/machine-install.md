# Technical

`tools/install/` turns a repo checkout into a working MagnaFlow machine:
the four tools published under their short names, on `PATH`, a machine
config in the user config dir, and the daemons wired to start. There are
two small scripts, one per OS, kept apart on purpose: two honest
scripts beat one clever one. Neither needs admin or sudo. Both need the
.NET 10 SDK.

**Installing and updating are the same command.** A re-run publishes
first, then stops the daemons, swaps in the new build and restarts what
it stopped. A running binary can be locked, and a running daemon keeps
its old code and config in memory, so an update only lands after a
restart.

**Publish first, stop second.** All four tools are published into
`<install-dir>/.staging/<tool>/` while the daemons keep running. Only
when every publish succeeded are the daemons stopped and each
`<install-dir>/<tool>/` replaced by its staged copy. A failed publish
removes the staging dir, names the tool that failed and exits non-zero,
with the running daemons and the installed tools untouched. A broken
build never takes MagnaFlow down, which is what makes an unattended
install (the Linux [self-update](#self-update)) safe.

## Shared rules

- Each tool is published in Release (via `.staging/`) to
  `<install-dir>/<tool>/`, with `AssemblyName` set to the short name: `mf-run`, `mf-worker`,
  `mf-watch`, `mf-cockpit`. The bare names used as config defaults then
  resolve via `PATH`.
- The flat `mf-cockpit.yml` that the cockpit build drops next to its
  binary is deleted from the install. The user config dir copy is then
  the config in use, see
  [machine config](machine-config.md#lookup-order).
- The machine config is **seeded only if missing**. Once
  `magnaflow.yml` exists in the user config dir, it is the live config
  (hand edits, projects added by the cockpit), and no re-run ever
  touches it. To pick up a template change: edit the live file, or
  delete it and re-run.
- Stopping covers the old assembly names too (`MagnaFlow.MfCockpit`,
  `MagnaFlow.MfWatch`), for pre-install builds still running.

## Windows: `install.ps1`

| | |
|---|---|
| Install dir | `C:\Tools\MagnaFlow\<tool>\` (`-InstallDir`) |
| PATH | the four tool folders are appended to the *user* PATH. New terminals only |
| Config seed | `%APPDATA%\MagnaFlow\magnaflow.yml` from `-MachineConfig` |
| Start | `start-magnaflow.ps1`, copied next to the tools |
| Shortcuts | "MagnaFlow" on the desktop and in the Start menu, minimized, icon `assets/magnaflow-logo.ico` (copied as `magnaflow.ico`) |
| Stop | `Stop-Process -Force` on every cockpit and watcher process, by name |

`start-magnaflow.ps1` starts the cockpit and **one** watcher, each
minimized in its own console window, then opens the cockpit URL. Closing
a window stops that daemon. The watcher's project is the script's
`-Project` parameter. It is idempotent: a daemon that already runs is
left alone. It refuses to start the cockpit when the port is held by
another process, and names that process. An old build holding the port
used to make every config change look ignored.

- The default `-MachineConfig` points into one specific project repo.
  There is no Windows template in `tools/install/`.
- The re-run restarts through `start-magnaflow.ps1` whenever *any*
  daemon was running. Before stopping, it reads every running
  watcher's `--project` from its command line. After
  `start-magnaflow.ps1`, it restarts each of those projects that is
  not already watched, the way the cockpit toggle does: hidden, with
  `--project <root>` and the project as working directory. A watcher
  started without `--project` cannot be traced back to a project, so
  it is named in a warning and not restarted.
- `Stop-Process` kills only the watcher, not its children. A worker run
  in flight keeps running orphaned while a new watcher starts.

## Linux: `install.sh`

```text
install.sh [--install-dir <dir>] [--config <path>] [--self-update] [--dry-run]
```

| | |
|---|---|
| Install dir | `~/tools/magnaflow/<tool>/` |
| PATH | symlinks in `~/.local/bin/`, with a warning if that is not on `PATH` |
| Config seed | `~/.config/magnaflow/magnaflow.yml` from `--config`, default `tools/install/magnaflow.linux.yml` |
| Daemons | systemd `--user` units: `mf-cockpit.service` (`Restart=on-failure`, enabled) and the template `mf-watch@.service` (`Restart=no`) |
| Stop | `systemctl --user stop` for the cockpit and every running `mf-watch@*` instance, then `pkill -x` on exact names for anything outside systemd |
| Logs | the journal: `journalctl --user -u mf-cockpit`, `-u mf-watch@<escaped>`, `-u mf-selfupdate` |
| Installed commit | `<install-dir>/installed-commit`: the repo's `HEAD`, read before publishing, written by every successful run (manual or automatic) |

- The template `magnaflow.linux.yml` is the worker-machine posture:
  `git_sync: true` and the cockpit bound to `0.0.0.0` (no auth; firewall
  or VPN is the operator's job). Its tool paths use the placeholder
  `/home/magnaflow/tools/magnaflow`, which is replaced by the real
  install dir on seeding. `cockpit.projects` is not replaced. It is an
  example to edit. The repo copy is never read by a tool.
- Both units set `PATH` (incl. `~/.local/bin`, `~/.dotnet/tools`) and
  `SSH_AUTH_SOCK` explicitly. A lingering user manager can start before
  login has imported the shell environment, and git, systemctl and
  Claude must resolve the same either way.
- Watch instances are not enabled by the installer. Enabling one is the
  cockpit toggle or
  `systemctl --user enable --now mf-watch@$(systemd-escape <path>).service`,
  see [watch supervision](watch-supervision.md).
- A re-run restarts exactly the cockpit and the watch instances that
  were running before it.
- `--self-update` additionally installs the self-update timer, see
  below. Without it an existing timer is left exactly as it is, neither
  rewritten nor removed.
- `--dry-run` prints every action and changes nothing. It works without
  the SDK, or from Git Bash on Windows.
- A headless machine needs `loginctl enable-linger $USER`, or the user
  services stop at logout. The script prints that hint and does not run
  it.

### Self-update (opt-in) {#self-update}

`install.sh --self-update` installs `mf-selfupdate.timer` (every 5
minutes, first tick 2 minutes after boot) and the oneshot
`mf-selfupdate.service`, which runs `<install-dir>/mf-selfupdate.sh`
(copied from `tools/install/`) with the repo root, install dir and
config of that install on its command line. It is its own unit, never
inside a watcher's control group, so the install stopping the watchers
does not stop it.

Each tick takes exactly one decision, in this order:

1. No `installed-commit` → skipped.
2. `HEAD` equals the installed commit → skipped.
3. `git diff --name-only <installed>..HEAD -- tools/` is empty → skipped.
   Docs-only commits never install. A diff that fails (the installed
   commit left the history) counts as a change.
4. `HEAD` is the commit whose install failed last
   (`<install-dir>/selfupdate-failed-commit`) → skipped. A failed
   install is retried only after a new commit, not every tick.
5. Any `docs/prompts/*-cmd-*.md` with `status: running` in its
   frontmatter, in the project of every running `mf-watch@` instance
   (unescaped like `%I`) or in the repo itself → skipped. Stopping a
   watcher kills its worker, so a stale `running` left by a killed
   worker blocks self-update until a human resets it.
6. Otherwise it runs the repo's `install.sh --self-update` with the same
   install dir and config. Success clears the failed-commit file; a
   failure writes `HEAD` into it. Publish-first means a failure leaves
   everything running.

It reads the local `HEAD` only and never fetches. Pulling is the
watcher's `git_sync`. Every decision is one line, timestamped, on stdout
(the journal) and in `<install-dir>/selfupdate.log`, which is cut back
to its last 1000 lines: `skipped: <why>`, `installed: <old>..<new>`,
`failed: <why>`. A skip identical to the last logged decision is not
written again, so a quiet repo shows one `skipped: no new commit` line
until something changes. `install.sh`'s own output goes to the journal only.
Every tick exits 0, failures included; the log carries the verdict.
Switching it off is `systemctl --user disable --now mf-selfupdate.timer`.

## Scope

The units and shortcuts supervise MagnaFlow's own daemons only. A target
project's dev and debug processes belong to
[mf-run](../run/mf-run.md), driven by the worker. Supervising those too
would hide the very crashes the worker has to see.

## Dogfooding: a worker run never installs

This repo is a MagnaFlow project, so its own watcher dispatches workers
that change tool code. A worker run must **never** trigger an install. An
install stops every watcher, including the one running that worker, and
on Windows `Stop-Process` does not take the child with it. This is why
this repo's `.magnaflow/config.yml` has no `run:` section. Rolling out
new tool code is a manual `install.ps1` / `install.sh`, or on Linux the
opt-in [self-update](#self-update) timer: a separate actor outside every
watcher, which installs only once no command is `running`.

DRAFT: generated from code, not human-reviewed.

Code: tools/install/install.ps1, tools/install/install.sh, tools/install/mf-selfupdate.sh, tools/install/start-magnaflow.ps1, tools/install/magnaflow.linux.yml
