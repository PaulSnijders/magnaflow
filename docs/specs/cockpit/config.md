# Technical

`config.html?p=<name>` shows the cockpit's own resolved setup, edits the
project's `.magnaflow/config.yml`, and holds Remove project. Appearance
follows [design](../concepts/design.md#shared-conventions). For the two
config files and their lookup order, see
[machine and project config](../concepts/machine-config.md).

## Cockpit

Read-only: which `magnaflow.yml` this process loaded, the effective
`run.command` and `chat.command`, and the last 100 lines of the
cockpit's own log. It exists so that a spawn failure elsewhere ("could
not start '…'") can be explained from the browser, without a shell on
the cockpit host. The machine config itself is never editable here.

## Summary

A parsed, read-only view: build and test commands, max attempts
(3 when unset), and the `run.services` names. Parsing is tolerant, so a
file that is already broken on disk still shows something.

## .magnaflow/config.yml

A raw YAML textarea, deliberately not a form. A form would regenerate
the YAML and lose comments and ordering, and then the cockpit would hold
state the file does not. The endpoint takes no path parameter; the only
editable file is exactly this one. A missing file is a 404, and the page
offers no way to create it.

`PUT /api/projects/{name}/config` with `{content, baseHash}` checks, in
order:

1. `baseHash` (a SHA-256 of the content from the GET) still matches the
   file on disk. Otherwise 409: reload and redo.
2. The content parses as YAML. Otherwise 400 with line and column.
3. The content is at most 256 KB. Otherwise 400.

Then it writes the file and commits that one file
(`cockpit: edit config`). Unknown top-level keys (anything except
`build`, `test`, `defaults`, `agent`, `run`) come back as a warning,
never a refusal: the file is shared by tools with different
vocabularies. Ctrl+Enter and Ctrl+S save.

While a command is `running`, a banner says the worker already read the
config, so a save takes effect on the next run. It warns and never
blocks.

A dirty editor is never overwritten by a live refresh. An on-disk change
then shows a "changed on disk" note instead, with two buttons:

- **Reload** loads the file from disk and discards the edits.
- **Overwrite** fetches the current disk hash, then saves the editor's
  content against it, replacing the version on disk.

Save itself keeps the concurrency check: while the note is up, a plain
Save still gets the 409.

## Remove project

It unregisters and never deletes. The working copy, its git history and
its `.magnaflow/` stay on disk, and the confirm dialog says so.
Re-adding is the [Add project](index.md#add-project) → Existing flow.
The project's scratchpad file is kept as well and reappears on re-add
under the same name.

`DELETE /api/projects/{name}`:

- 409 while any command in the project is `running`, because
  unregistering would hide a live worker.
- It is a text edit removing exactly that one entry from
  `magnaflow.yml`, the mirror of the add. It writes a `.bak` first, then
  re-parses and verifies that the entry is gone and every other entry
  survived. On any failure it restores and answers 500. The legacy
  `mf-cockpit.yml` filename is refused (400). It shares the add's
  single config-write lock.
- Only after the edit verifies does it drop the in-memory registration
  and dispose the project's file watcher. Then it broadcasts SSE
  `projects`, and the browser goes to `index.html`.

A running watcher does not block removal. It outlives the cockpit
forgetting the project, so the page and the confirm dialog both say so
and point to the Watcher card's Stop. See
[watch supervision](../concepts/watch-supervision.md).

DRAFT: generated from code, not human-reviewed.

Why: decisions/0008-cockpit-run-and-config.md
