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

## Files

Under the note sits a file drop: a way to move a file from one machine
to another through the cockpit host. It is for temporary transfer, not
storage, so it is deliberately bare.

The files live in `scratchpad/<dirname>.files/`, next to the note and
named by the same sanitizer and hash fallback. They are binary, which
the note is not; that is acceptable for the same reason the note is: a
plain directory any file manager can open, outside every working copy,
read by no tool. Removing a project leaves its files in place, like
the note.

The page shows, below the note:

- an **Upload** button that takes one or more files, and the same for
  files dropped anywhere on the file card;
- per upload a row with the file name and progress, until it lands;
- the list of files, newest first: name, size, upload time, a
  **Download** button and a **Delete** button.

Delete removes the file at once, without a confirm. An upload whose
name already exists replaces that file. The list reloads after every
upload or delete, when the tab becomes visible again, and on a Refresh
button; there is no live push, as for the note.

API, `{name}` the project and `{file}` the stored file name:

- `GET /api/projects/{name}/files` → `[{name, size, uploadedAt}]`,
  newest first; an absent directory is an empty list.
- `PUT /api/projects/{name}/files/{file}` with the raw file as body
  stores it and returns the entry, with the name as stored. Over
  200 MB → 413.
- `GET /api/projects/{name}/files/{file}` streams it as an attachment
  under its name; unknown → 404.
- `DELETE /api/projects/{name}/files/{file}` → 204; unknown → 404.

The name is the one place a path parameter enters the scratchpad. The
server stores the last path segment only, with characters invalid on
Windows or Linux and control characters replaced by `_`; an empty,
dot-only or reserved (`CON`, `NUL`, …) result is refused (400). An
upload is written to a hidden temporary file in the same directory and
renamed into place when complete, so an aborted upload never shows in
the list; the list skips those temporary files.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0013-cockpit-quality-of-life.md
