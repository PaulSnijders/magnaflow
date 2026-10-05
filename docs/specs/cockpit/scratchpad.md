# Technical

`scratchpad.html?p=<name>` is one plain-text notepad per project. It is
kept on the cockpit host, so the same note shows from every machine that
opens the dashboard. Appearance follows
[design](../concepts/design.md#shared-conventions).

## Storage, outside git by design

The file is `scratchpad/<dirname>.md` in the directory of the loaded
`magnaflow.yml` (`CockpitConfig.ConfigPath`). This holds even when that
file does not exist and the cockpit runs on defaults. The scratchpad
must never land in a working copy: an untracked file there would dirty
the tree the worker guards.

It is the one store the cockpit keeps outside git. That is acceptable
because it is plain text holding notes, not system state, and no tool
ever reads it.

The file name is the project name run through the Add project
sanitizer. An empty or reserved result (`CON`, `NUL`, …) becomes a
stable `_<12 hex>` hash instead. The endpoint takes no path parameter,
so there is nothing to escape. Two project names that sanitize alike
(`a b` and `a-b`) share one note. Removing a project leaves its note in
place.

## Save contract

`GET /api/projects/{name}/scratchpad` returns `{content, hash, savedAt}`.
A missing file reads as empty content with the hash of `""`.
`PUT` with `{content, baseHash}` refuses a hash mismatch first (409),
then content over 256 KB (400).

The page autosaves after about 1.5 s idle, on blur, and when the tab is
hidden. Ctrl+S and Ctrl+Enter save at once. A small indicator shows
`saved hh:mm`, `unsaved`, `saving…` or the error.

On a 409 the user chooses, so nothing is lost silently: load the
server's version (discarding local edits), or keep their own, which
re-reads the server hash and overwrites.

## No live sync

No file watcher covers the scratchpad directory, so no SSE event exists
for it. The hash check on save is the whole concurrency model. A second
browser does not see an edit until it reloads or hits the 409. The
summary bar still follows `lane` and `watch` events.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0013-cockpit-quality-of-life.md
