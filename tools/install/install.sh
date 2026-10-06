#!/usr/bin/env bash
#
# MagnaFlow one-time (re-runnable) machine install — Linux worker counterpart
# of install.ps1. Same flow, Linux idioms:
#
#   1. Publish the four tools (Release) into $INSTALL_DIR/.staging/<tool>/,
#      renamed to their canonical short names (mf-run, mf-worker, mf-watch,
#      mf-cockpit), while the daemons keep running. A failed publish removes
#      the staging dir and exits non-zero with nothing stopped or replaced —
#      unattended (self-update), a broken build must never take MagnaFlow down.
#   2. Only then stop the running daemons (systemctl --user, falling back to
#      pkill on exact process names) — a running daemon keeps its old
#      code/config in memory, so an update only lands after a restart — and
#      swap each $INSTALL_DIR/<tool>/ for its staged copy.
#   3. Symlink the four binaries into ~/.local/bin/.
#   4. Seed the machine config into ~/.config/magnaflow/magnaflow.yml — the one
#      location every tool checks regardless of where its binary lives. Only
#      happens the first time, when that file doesn't exist yet: once it's
#      there, it's the user's live config and a re-run must never clobber it.
#   5. Generate + install systemd --user unit files for mf-cockpit and (as a
#      template) mf-watch@, enable the cockpit unit, and (with --self-update)
#      the mf-selfupdate timer.
#   6. Restart whatever was running before step 2 and record the installed
#      commit in $INSTALL_DIR/installed-commit — same update path as
#      install.ps1: one re-run after a code or config change re-publishes and
#      restarts on the new version.
#
# Usage:
#   tools/install/install.sh [--install-dir <dir>] [--config <path>] [--self-update] [--dry-run]
#
# Requires the .NET 10 SDK (the tools target net10.0) and a user systemd
# instance (systemctl --user). No sudo — this is a user-level install only.
set -euo pipefail

# This script lives at <repo>/tools/install/, so the repo root is two up.
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

install_dir="$HOME/tools/magnaflow"
machine_config="$script_dir/magnaflow.linux.yml"
self_update=0
dry_run=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [--install-dir <dir>] [--config <path>] [--self-update] [--dry-run]

  --install-dir <dir>  Where the published tools land (default: $HOME/tools/magnaflow)
  --config <path>      Source machine config used to seed ~/.config/magnaflow/magnaflow.yml
                        the first time it doesn't exist yet (default: tools/install/magnaflow.linux.yml)
  --self-update        Also install the mf-selfupdate timer: every 2 minutes it re-runs this
                        install when a new commit changed tools/ and no command is running
  --dry-run            Print the actions that would be taken without doing them
  -h, --help           Show this help
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --install-dir) install_dir="$2"; shift 2 ;;
        --config) machine_config="$2"; shift 2 ;;
        --self-update) self_update=1; shift ;;
        --dry-run) dry_run=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [[ ! -f "$machine_config" ]]; then
    echo "error: machine config not found: $machine_config" >&2
    exit 2
fi
# The self-update unit re-runs this script with the same --config, so keep it absolute.
machine_config="$(cd "$(dirname "$machine_config")" && pwd)/$(basename "$machine_config")"

echo "== MagnaFlow install (repo: $repo_root -> $install_dir) =="
installed_commit="$(git -C "$repo_root" rev-parse HEAD 2>/dev/null || true)"

# act runs (or, in --dry-run, just prints) a simple argv command. Heredoc-based
# steps (unit file generation) guard themselves inline instead, since a heredoc
# can't be captured as "$@".
act() {
    if [[ "$dry_run" -eq 1 ]]; then
        printf '+ %s\n' "$*"
    else
        "$@"
    fi
}

have_systemctl_user() {
    command -v systemctl >/dev/null 2>&1 && systemctl --user status >/dev/null 2>&1
}

# --- 1. publish the tools into staging (daemons keep running) ----------------
tool_names=(mf-run mf-worker mf-watch mf-cockpit)
tool_csproj=(
    "tools/mf-run/src/MagnaFlow.MfRun/MagnaFlow.MfRun.csproj"
    "tools/worker-controller/src/MagnaFlow.WorkerController/MagnaFlow.WorkerController.csproj"
    "tools/mf-watch/src/MagnaFlow.MfWatch/MagnaFlow.MfWatch.csproj"
    "tools/mf-cockpit/src/MagnaFlow.MfCockpit/MagnaFlow.MfCockpit.csproj"
)
staging_dir="$install_dir/.staging"

# A leftover from an interrupted run would otherwise mix into this one.
if [[ -e "$staging_dir" ]]; then
    act rm -rf "$staging_dir"
fi

for i in "${!tool_names[@]}"; do
    name="${tool_names[$i]}"
    out="$staging_dir/$name"
    echo "-- publishing $name -> $out"
    if ! act dotnet publish "$repo_root/${tool_csproj[$i]}" -c Release -o "$out" "-p:AssemblyName=$name" --nologo; then
        rm -rf "$staging_dir"
        echo "error: dotnet publish failed for $name — nothing was stopped, the installed tools in $install_dir are untouched" >&2
        exit 1
    fi
done

# The cockpit build copies a default mf-cockpit.yml next to its binary (legacy
# pre-fase-7 config). Remove it from the install so ~/.config/magnaflow/
# magnaflow.yml is unambiguously the config in use.
legacy_cockpit_yml="$staging_dir/mf-cockpit/mf-cockpit.yml"
if [[ "$dry_run" -eq 1 ]]; then
    echo "+ rm -f $legacy_cockpit_yml"
elif [[ -f "$legacy_cockpit_yml" ]]; then
    rm -f "$legacy_cockpit_yml"
fi

# --- 2. stop running daemons (restarted again at the end), swap in the build -
# A running daemon keeps its old code/config in memory, so an update only
# lands after a restart. Stopping happens only now that every publish
# succeeded.
was_cockpit_running=0
declare -a watch_instances_running=()

if have_systemctl_user; then
    if systemctl --user is-active --quiet mf-cockpit.service 2>/dev/null; then
        was_cockpit_running=1
    fi
    while IFS= read -r unit; do
        [[ -n "$unit" ]] && watch_instances_running+=("$unit")
    done < <(systemctl --user list-units --type=service --state=running --no-legend --plain 'mf-watch@*.service' 2>/dev/null | awk '{print $1}')
fi

if [[ "$was_cockpit_running" -eq 1 || "${#watch_instances_running[@]}" -gt 0 ]]; then
    echo "-- stopping running MagnaFlow daemons (restarted after the install)"
    [[ "$was_cockpit_running" -eq 1 ]] && act systemctl --user stop mf-cockpit.service
    if [[ "${#watch_instances_running[@]}" -gt 0 ]]; then
        for unit in "${watch_instances_running[@]}"; do
            act systemctl --user stop "$unit"
        done
    fi
fi

# Fallback for anything not under systemd (a manual `dotnet run`, or a
# pre-install build that ran under its assembly name) — exact-name match only,
# same intent as the Windows Get-Process/Stop-Process list.
for name in mf-cockpit mf-watch MagnaFlow.MfCockpit MagnaFlow.MfWatch; do
    if [[ "$dry_run" -eq 1 ]]; then
        if pgrep -x "$name" >/dev/null 2>&1; then
            echo "+ pkill -x $name"
        fi
    elif pkill -x "$name" 2>/dev/null; then
        echo "-- stopped stray process: $name"
    fi
done
if [[ "$dry_run" -eq 0 ]]; then
    sleep 1
fi

for name in "${tool_names[@]}"; do
    act rm -rf "$install_dir/$name"
    act mv "$staging_dir/$name" "$install_dir/$name"
done
act rm -rf "$staging_dir"
echo "-- installed into $install_dir: ${tool_names[*]}"

# --- 3. symlink binaries into ~/.local/bin ------------------------------------
bin_dir="$HOME/.local/bin"
act mkdir -p "$bin_dir"
for name in "${tool_names[@]}"; do
    act ln -sf "$install_dir/$name/$name" "$bin_dir/$name"
done
echo "-- symlinked into $bin_dir: ${tool_names[*]}"

case ":$PATH:" in
    *":$bin_dir:"*) ;;
    *) echo "-- warning: $bin_dir is not on PATH — add 'export PATH=\"\$HOME/.local/bin:\$PATH\"' to your shell rc" ;;
esac

# --- 4. machine config -> ~/.config/magnaflow (seeded once, never overwritten) -
# The template's tool paths (watch.worker.command, cockpit.run.command) are
# written against the placeholder /home/magnaflow/tools/magnaflow — the
# default install layout for a user literally named "magnaflow". install.sh
# already knows the real $install_dir at this point, so it substitutes the
# placeholder for it here rather than making every operator hand-edit paths
# the script itself could fill in. cockpit.projects is left untouched: which
# projects to watch is genuinely operator-specific and has no derivable
# default — add/remove those by hand or via the cockpit's own "Add project"
# button after install.
config_dir="$HOME/.config/magnaflow"
config_target="$config_dir/magnaflow.yml"
placeholder_install_dir="/home/magnaflow/tools/magnaflow"
act mkdir -p "$config_dir"
if [[ -f "$config_target" ]]; then
    echo "-- machine config already exists, leaving it as-is: $config_target"
elif [[ "$dry_run" -eq 1 ]]; then
    echo "+ seed machine config: $machine_config -> $config_target (substituting $placeholder_install_dir -> $install_dir)"
else
    sed "s#$placeholder_install_dir#$install_dir#g" "$machine_config" > "$config_target"
    echo "-- machine config seeded: $machine_config -> $config_target (tool paths adjusted for $install_dir)"
fi

# --- 5. systemd --user units ---------------------------------------------------
systemd_user_dir="$HOME/.config/systemd/user"
act mkdir -p "$systemd_user_dir"

cockpit_unit="$systemd_user_dir/mf-cockpit.service"
watch_unit="$systemd_user_dir/mf-watch@.service"

if [[ "$dry_run" -eq 1 ]]; then
    echo "+ write $cockpit_unit"
    echo "+ write $watch_unit"
else
    cat > "$cockpit_unit" <<EOF
[Unit]
Description=MagnaFlow cockpit (dashboard + chat)
After=network.target

[Service]
Type=simple
ExecStart=$install_dir/mf-cockpit/mf-cockpit
WorkingDirectory=$install_dir/mf-cockpit
# A lingering user manager can start this unit before the graphical login has
# imported the interactive shell environment. Keep PATH and the stable GNOME
# keyring socket explicit so git, systemctl/systemd-escape and Claude resolve
# the same way before and after a manual restart.
Environment="PATH=%h/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:%h/.dotnet/tools"
Environment="SSH_AUTH_SOCK=%t/keyring/ssh"
Restart=on-failure
RestartSec=2

[Install]
WantedBy=default.target
EOF

    # Templated on the watched project's path: `%i` is the instance name given
    # at enable time (systemd-escaped), `%I` is mf-run/mf-watch's unescaped
    # form — passed straight through as --project so one unit template covers
    # any number of watched projects, one instance each.
    cat > "$watch_unit" <<EOF
[Unit]
Description=MagnaFlow watch daemon (%i)
After=network.target

[Service]
Type=simple
ExecStart=$install_dir/mf-watch/mf-watch --project %I
Environment="PATH=%h/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:%h/.dotnet/tools"
Environment="SSH_AUTH_SOCK=%t/keyring/ssh"
Restart=no

[Install]
WantedBy=default.target
EOF
fi
echo "-- systemd units: $cockpit_unit, $watch_unit"

# Opt-in self-update: a timer-driven oneshot unit of its own — never inside a
# watcher's cgroup, so stopping the watchers (step 2) doesn't stop it. Without
# --self-update an existing timer is left exactly as it is. The script is
# copied into the install dir so the timer runs the installed version, and it
# re-runs this script with --self-update, so an automatic install refreshes
# the script and these units too.
if [[ "$self_update" -eq 1 ]]; then
    selfupdate_script="$install_dir/mf-selfupdate.sh"
    selfupdate_service="$systemd_user_dir/mf-selfupdate.service"
    selfupdate_timer="$systemd_user_dir/mf-selfupdate.timer"
    act install -m 755 "$script_dir/mf-selfupdate.sh" "$selfupdate_script"
    if [[ "$dry_run" -eq 1 ]]; then
        echo "+ write $selfupdate_service"
        echo "+ write $selfupdate_timer"
    else
        cat > "$selfupdate_service" <<EOF
[Unit]
Description=MagnaFlow self-update (re-install when tools/ changed and nothing runs)

[Service]
Type=oneshot
ExecStart=$selfupdate_script --repo $repo_root --install-dir $install_dir --config $machine_config
Environment="PATH=%h/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:%h/.dotnet/tools"
Environment="SSH_AUTH_SOCK=%t/keyring/ssh"
TimeoutStartSec=15min
EOF
        cat > "$selfupdate_timer" <<EOF
[Unit]
Description=MagnaFlow self-update check, every 2 minutes

[Timer]
OnBootSec=2min
OnUnitInactiveSec=2min

[Install]
WantedBy=timers.target
EOF
    fi
    echo "-- self-update: $selfupdate_service, $selfupdate_timer (log: $install_dir/selfupdate.log)"
fi

act systemctl --user daemon-reload
act systemctl --user enable mf-cockpit.service
if [[ "$self_update" -eq 1 ]]; then
    act systemctl --user enable --now mf-selfupdate.timer
fi

# --- 6. restart whatever was running before step 2 ----------------------------
echo ''
if [[ "$was_cockpit_running" -eq 1 ]]; then
    act systemctl --user restart mf-cockpit.service
fi
if [[ "${#watch_instances_running[@]}" -gt 0 ]]; then
    for unit in "${watch_instances_running[@]}"; do
        act systemctl --user restart "$unit"
    done
fi

# The self-update timer compares HEAD against this to decide whether tools/
# changed since the last install — manual or automatic. It is the HEAD read
# before publishing, so a commit landing mid-install still counts as new.
if [[ -n "$installed_commit" ]]; then
    if [[ "$dry_run" -eq 1 ]]; then
        echo "+ record installed commit $installed_commit -> $install_dir/installed-commit"
    else
        echo "$installed_commit" > "$install_dir/installed-commit"
        echo "-- installed commit: $installed_commit"
    fi
else
    echo "-- warning: $repo_root is not a git checkout, installed-commit not recorded"
fi

if [[ "$was_cockpit_running" -eq 1 || "${#watch_instances_running[@]}" -gt 0 ]]; then
    echo "== Done. Cockpit + watcher restarted on the new version."
else
    echo "== Done. mf-cockpit.service is enabled (starts at login)."
    echo "   Start it now:    systemctl --user start mf-cockpit.service"
    echo "   Watch a project: systemctl --user enable --now mf-watch@\$(systemd-escape /path/to/project).service"
fi
echo "-- headless machine? run: loginctl enable-linger \$USER   (user services otherwise stop at logout)"
