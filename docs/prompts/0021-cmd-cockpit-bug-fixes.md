---
title: "mf-cockpit: safe and working links, endpoint guards, config overwrite, Add project fixes, live updates for new projects"
status: ready
created: 2026-10-05
---

## Context

The initial spec sync left BUG lines in `docs/specs/cockpit/` and in
`docs/specs/concepts/machine-config.md`. They are all small. Fix them in
this run, step by step. Where the cockpit test project cannot reach a
change (front-end JS), verify it live and say so in the rst.

## Task

1. **Markdown links** (`chat.md`, `specs.md`).
   - `wwwroot/assets/md.js` copies link targets verbatim, so a
     `javascript:` URL in an agent reply becomes clickable. Allow only
     `http:`, `https:`, `mailto:` and relative targets; render anything
     else as plain text.
   - `specs.html` leaves relative links raw, so `../concepts/design.md`
     opens `/concepts/design.md`, a 404. Resolve a relative `.md` link
     against the current spec's path and rewrite it to
     `specs.html?p=<project>&path=<resolved>`, keeping any `#anchor`. A
     link that leaves `docs/specs/` renders as plain text.
2. **Follow-up endpoint guard** (`command.md`). The card is shown only
   for `done` and `aborted`, but
   `POST /api/projects/{name}/commands/{id}/follow-up` accepts any
   parent. Refuse any other parent status with 409 and a clear `error`.
   Test: 409 for draft/ready/running/questions, success for done/aborted.
3. **Config editor conflict** (`config.md`). "Changed on disk — Save to
   overwrite" is wrong: the page keeps the old `baseHash`, so Save always
   gets a 409. Keep the concurrency check and offer two actions:
   - **Reload**: discard the edits.
   - **Overwrite**: fetch the current disk hash, then save against it.
   Make the note text match the buttons.
4. **Add project** (`index.md`, `concepts/machine-config.md`).
   - The dialog shows `warnings` after a success, and `output` after a
     failed scaffold. Today it shows only `error`.
   - The New tab's target preview joins root and name with the server's
     separator, not always `\`.
   - A legacy `mf-cockpit.yml` gets a 400 with the same message as
     Remove project, not a 500.
   - `MagnaflowYmlAppender`: today, a `magnaflow.yml` with flat
     root-level cockpit fields (no `cockpit:` key) gets a new
     `cockpit:` section, which hides the existing projects. Refuse that
     with a 400 that says to move the fields under `cockpit:`; do not
     migrate automatically. After an append, the verification checks
     that every previously listed project is still read.
   - Tests for the legacy 400 and for the appender.
5. **Live updates for a project added at runtime** (`index.md`).
   `ProjectWatchersHostedService` creates file watchers only at
   startup. Removal already disposes the removed project's watcher
   through `IProjectWatcherRegistry`; add the other half there: Add
   project starts the new project's watcher at once, with no restart.
   Test through the seam the existing tests use.
6. **Spec-first:** remove every BUG line in `chat.md`, `specs.md`,
   `command.md`, `config.md`, `index.md` and
   `concepts/machine-config.md`, and describe the new behaviour there.
