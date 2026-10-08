---
date: 2026-08-07
topic: cockpit
status: accepted
---

# 0013 — mf-cockpit v0.5 — design: quality of life

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

Daily use of the dashboard turned up nine small, independent annoyances:
pages going stale without F5 (worst over Tailscale or after laptop
sleep, when the SSE socket looks open but is dead), no at-a-glance "is
anything happening here?", service links reading `localhost` when
opened from another machine, no way to unregister a project or pull,
old `aborted` commands nagging forever, no notepad that follows you
between machines.

Constraints carried over from `0007-cockpit-design.md`: every write is
a human click, lands in an existing plain-text/git format and could be
done by hand. Frontend stays hand-written HTML/CSS/JS, no build step,
no CDN; shared behavior goes in the thin `Cockpit` helper in
`assets/app.js`. The agreed threshold for moving one page to Preact+htm
(ESM, no npm) is "hand-carrying re-render state for a third time on one
page"; this batch does not reach it.

## Options

Per item the alternatives were small; the ones genuinely weighed are
named under Choice. The cross-cutting question was whether the
scratchpad may break "no state that exists only inside the cockpit".

## Measurement

The invariant above, KISS (no new dependency, no JS test runner the
repo does not otherwise have), and keeping the fast summary path free
of `git` and mf-run subprocesses.

## Choice

All nine, as one release. Plain features with no trade-off: the index
"Latest" column (`specs/cockpit/index.md`) and Ctrl+Enter submitting
every primary text action (each page spec). The others:

### Live refresh

Pages refetch their own JSON and re-render, never `location.reload()`,
so open state survives. Because a dead socket is silent, the helper adds
a server heartbeat with a client watchdog, reconnect with backoff, a
slow polling fallback while disconnected, a refetch on tab-visible, and
a badge that says honestly whether it is live, reconnecting or polling.
A refresh never clobbers unsaved input: a dirty editor gets a "changed
on disk" note instead (`specs/cockpit/config.md`).

### Summary bar

One muted sticky row on every project page, fed only by the fast,
file-I/O summary. Later revised (`docs/prompts/0010-cmd-cockpit-page-load-cost.md`): the run chip and
git branch were dropped, because on every page but `project.html` the
bar was their only caller and spawned `git` and mf-run on each refetch.

### Service URLs use the host you came in on

Display only: a loopback host in a rendered URL becomes
`window.location.hostname`; config and mf-run are untouched, and the
configured value stays in the link's `title`. The rule is a pure
function verified live, not by a JS test runner.

### Remove project

It unregisters and never deletes; re-adding as "existing" is the undo.
Refused while a command is `running`, since that would hide a live
worker. A running watcher does not block, but is named, because it
outlives the cockpit forgetting the project. Current behavior:
`specs/cockpit/config.md`.

### `aborted` demands attention only as the highest id

Writing a follow-up, or simply moving on to the next number, is the
human saying "seen it". `questions` and stale `running` always count.
Current behavior: `specs/cockpit/index.md`.

### Git pull is `--ff-only` only

No merge, rebase, stash or autocommit from a button; refused on a dirty
tree or while a command runs; a credentials prompt fails fast instead
of wedging the request. git's own output is shown, since "not possible
to fast-forward" is information. Later extended with Sync; see
`specs/cockpit/project.md`.

### Scratchpad outside git

The one store the cockpit keeps outside git, next to the loaded
`magnaflow.yml`. Inside a working copy an untracked file would dirty
the tree the worker guards. It is permissible because it is plain text any editor
can read, and holds notes, not system state that any tool reads.
Concurrency is a hash check on save; cross-machine live sync was left
out because the 409 is enough of a safety net. Current behavior:
`specs/cockpit/scratchpad.md`.

### Not in scope

Scratchpad live sync, a rich-text notepad, undo for remove beyond
re-adding, a JS unit-test runner, auth (VPN/LAN stays the perimeter).
