---
date: 2026-07-14
topic: cockpit, onboarding
status: accepted
---

# 0009 — mf-cockpit v0.4 — design: Add project

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

Starting a project meant a terminal: make a directory, init git, copy
the spec kit, maybe run a code generator, then hand-edit
`magnaflow.yml` so the cockpit sees it. The goal: from the cockpit, add
an existing working copy, or a brand-new one that the cockpit scaffolds
and registers, so the first spec-kit command is a draft in the lane
before a terminal is opened. Two earlier lines were in the way: the
invariant of 0007-cockpit-design.md, and v0.3's exclusion of
machine-config editing as "self-surgery" (0008-cockpit-run-and-config.md).

## Options

- **Registration**: keep `magnaflow.yml` hand-edited only, or let the
  cockpit append one entry to `cockpit: projects:`.
- **How to write `magnaflow.yml`**: parse and regenerate, or a narrow
  text edit.
- **Templates**: one kind, or both a plain directory copy and an
  external generator command — both already existed in practice.
- **How far "start immediately" goes**: seed a draft, or also run the
  adopt prompt.

## Measurement

- **Same machine.** The v0.3 exclusion targeted a raw editor for a file
  that may belong to a different machine than the browser's. A project
  directory the cockpit creates necessarily lives on the cockpit's own
  machine, and registering it is one schema-known entry in the config
  file this process actually loaded. A narrow, structured append is not
  an editor.
- **No git undo.** `magnaflow.yml` is not in git; for the earlier cockpit writes git
  is the undo, here it cannot be. So the edit needs its own guards
  (backup, re-parse and verify, restore) and must keep comments and
  ordering — the same reasoning that made the config page a raw editor.
- **Security shape.** The request runs configured programs and creates
  directories. Templates live in operator-authored machine config
  (paths and installed generators are per machine by design); the
  browser sends only a template *name* and a project name, so nothing
  from the request is ever part of a spawned command line or a path
  outside `new_project.root`.
- **The human gate.** A draft is inert; `draft` → `ready` stays where it
  always was. Auto-running the adopt prompt would move that gate.

## Choice

The cockpit appends to `magnaflow.yml` as a text edit, never a
parse-and-regenerate, with a `.bak`, re-parse verification and restore;
everything else in the v0.3 exclusion stands — a raw machine-config
editor stays out. Both template kinds, resolved server-side by name (each entry under
`cockpit.new_project.templates`: `name`, `type: copy` with `source`, or
`type: command` with `command`, `args`, `timeout_seconds`).
The scaffold ends in a seeded *draft* through the normal draft write,
nothing auto-runs.

The order of the scaffold is the design, and one choice in it matters
most: **registration is last**. A failed scaffold never leaves a phantom
entry; it leaves the directory in place for diagnosis, and the human
deletes or fixes it. The inverse failure (scaffold done, append failed)
is self-healing: the directory is complete, so "Existing" registers it.
A missing `.git` on an existing copy is a warning, not a refusal — the
git card degrades visibly, which is the honest signal. A dirname is
sanitized from the name with no override field (rename by hand later —
KISS). The legacy `mf-cockpit.yml` is refused rather than edited: the
deprecation path does not grow new features.

Not in scope (v0.4): registering the project with mf-watch (`watch:`
section, possibly another machine — the project page's silent watcher
line is the prompt to configure it); a template-management UI
(`magnaflow.yml` in an editor is the management UI); delete, rename or
unregister (unregister was added later, 0013-cockpit-quality-of-life.md);
a dirname override; auto-running the adopt prompt; the raw
machine-config editor.

Current behavior: specs/cockpit/index.md (Add project: dialog,
validation order, scaffold steps, registration),
concepts/machine-config.md (`cockpit.new_project`, the
`magnaflow.yml` writes and their guards).
