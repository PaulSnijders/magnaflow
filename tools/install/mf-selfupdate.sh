#!/usr/bin/env bash
#
# MagnaFlow self-update tick — run by mf-selfupdate.service (a oneshot unit on
# mf-selfupdate.timer, every 2 minutes), installed by `install.sh --self-update`.
# One decision per tick, one log line per decision:
#
#   skipped: no installed-commit       install.sh never recorded a commit here
#   skipped: no new commit             HEAD is the installed commit
#   skipped: no tools/ change ...      the new commits touch docs only
#   skipped: install of X failed ...   this HEAD failed before; wait for a new commit
#   skipped: <cmd> is running          a worker is busy in a watched project or the repo
#   installed: old..new                install.sh succeeded
#   failed: ...                        install.sh failed (it left everything running)
#
# Every tick exits 0, failures included: the verdict is in the log, and a
# failed oneshot unit is not something the timer should have to recover from.
# It only reads the local HEAD and never fetches: pulling is the watcher's
# git_sync job. It runs as its own unit, outside every watcher's cgroup, so the
# install stopping the watchers does not stop it.
#
# Usage:
#   mf-selfupdate.sh --repo <repo-root> --install-dir <dir> --config <path>
#
# MF_SELFUPDATE_PROJECTS (newline-separated paths) replaces the lookup of the
# running mf-watch@ instances — for testing against fake projects.
set -uo pipefail

repo_root=""
install_dir=""
machine_config=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --repo) repo_root="$2"; shift 2 ;;
        --install-dir) install_dir="$2"; shift 2 ;;
        --config) machine_config="$2"; shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done
if [[ -z "$repo_root" || -z "$install_dir" || -z "$machine_config" ]]; then
    echo "usage: $(basename "$0") --repo <repo-root> --install-dir <dir> --config <path>" >&2
    exit 2
fi

commit_file="$install_dir/installed-commit"
failed_file="$install_dir/selfupdate-failed-commit"
log_file="$install_dir/selfupdate.log"
log_keep=1000

# log writes one decision to stdout (the journal) and to the log file, which is
# cut back to its last $log_keep lines — a skip every 2 minutes adds up.
log() {
    local line
    line="$(date -Is) $*"
    echo "$line"
    mkdir -p "$install_dir"
    echo "$line" >> "$log_file"
    if [[ "$(wc -l < "$log_file")" -gt "$log_keep" ]]; then
        tail -n "$log_keep" "$log_file" > "$log_file.tmp" && mv "$log_file.tmp" "$log_file"
    fi
}

short() { printf '%s' "${1:0:7}"; }

# running_cmd prints the first docs/prompts/*-cmd-*.md under $1 whose
# frontmatter (the first ----delimited block) says `status: running`.
running_cmd() {
    local f
    for f in "${1%/}"/docs/prompts/*-cmd-*.md; do
        [[ -f "$f" ]] || continue
        if awk '
            { sub(/\r$/, "") }
            NR == 1 { if ($0 != "---") exit 1; next }
            $0 == "---" { exit 1 }
            /^status:[ \t]*running[ \t]*(#.*)?$/ { found = 1; exit 0 }
            END { exit found ? 0 : 1 }
        ' "$f"; then
            echo "$f"
            return 0
        fi
    done
    return 1
}

# watched_projects lists the project paths of the running mf-watch@ instances
# (the unescape is the one %I does for --project), or MF_SELFUPDATE_PROJECTS.
watched_projects() {
    if [[ -n "${MF_SELFUPDATE_PROJECTS+x}" ]]; then
        printf '%s\n' "$MF_SELFUPDATE_PROJECTS"
        return
    fi
    local unit instance
    systemctl --user list-units --type=service --state=running --no-legend --plain 'mf-watch@*.service' 2>/dev/null \
        | awk '{print $1}' \
        | while IFS= read -r unit; do
            instance="${unit#mf-watch@}"
            instance="${instance%.service}"
            systemd-escape --unescape -- "$instance"
        done
}

# --- decisions ---------------------------------------------------------------
if [[ ! -s "$commit_file" ]]; then
    log "skipped: no installed-commit in $install_dir (run install.sh once)"
    exit 0
fi
installed="$(tr -d '[:space:]' < "$commit_file")"

if ! head="$(git -C "$repo_root" rev-parse HEAD 2>/dev/null)"; then
    log "failed: cannot read HEAD of $repo_root"
    exit 0
fi

if [[ "$head" == "$installed" ]]; then
    log "skipped: no new commit ($(short "$installed"))"
    exit 0
fi

# A diff that fails (the installed commit left the history, e.g. after a force
# push) counts as a change: HEAD is still what should be installed.
if changed="$(git -C "$repo_root" diff --name-only "$installed..$head" -- tools/ 2>/dev/null)" && [[ -z "$changed" ]]; then
    log "skipped: no tools/ change in $(short "$installed")..$(short "$head")"
    exit 0
fi

if [[ -s "$failed_file" && "$(tr -d '[:space:]' < "$failed_file")" == "$head" ]]; then
    log "skipped: install of $(short "$head") failed, waiting for a new commit"
    exit 0
fi

while IFS= read -r project; do
    [[ -n "$project" ]] || continue
    if cmd="$(running_cmd "$project")"; then
        log "skipped: $cmd is running"
        exit 0
    fi
done < <(watched_projects; echo "$repo_root")

# --- install -------------------------------------------------------------------
# install.sh's own output goes to the journal; the log file gets the verdict.
"$repo_root/tools/install/install.sh" --self-update --install-dir "$install_dir" --config "$machine_config"
rc=$?
if [[ "$rc" -eq 0 ]]; then
    rm -f "$failed_file"
    log "installed: $(short "$installed")..$(short "$head")"
else
    echo "$head" > "$failed_file"
    log "failed: install.sh exited $rc for $(short "$installed")..$(short "$head") (see journalctl --user -u mf-selfupdate)"
fi
