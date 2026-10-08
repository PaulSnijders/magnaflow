---
date: 2026-07-15
topic: config, machine
status: accepted
---

# 0012 — One machine config: magnaflow.yml

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

Two daemons each had their own machine-level config file next to the
binary: `mf-watch.yml` and `mf-cockpit.yml`. MagnaFlow's config has
exactly two axes: per project (`.magnaflow/config.yml`, in git, already
one file shared by worker and mf-run with their own sections) and per
machine. The machine axis had become two files, and every new daemon
would add another.

## Options

1. **Keep one file per tool.**
2. **One `magnaflow.yml` per machine with a section per tool**, each
   tool keeping its own loader.
3. **One file plus a shared config library** used by every tool.
4. **Centralize project state into the machine file too.**

## Measurement

- Mirror the project axis: one file, sections owned by their readers;
  a future tool claims a new section without touching the others.
- The decoupling rule: no shared library between tools (rules out 3).
- Project configs stay per repo, self-contained and git-native, and
  machine-specific values (`git_sync`, `notify_command`, the cockpit's
  project paths) stay out of git. The merge consolidates files; it
  does not centralize project state (rules out 4).
- Zero behavior change for a machine still running the old files: the
  legacy path is the proof, not an afterthought.

## Choice

Option 2. Current behavior, including the lookup order, legacy file
names and old-format tolerance: `specs/concepts/machine-config.md`.

- Each tool reads only its own section and ignores the rest, the
  tolerant posture both loaders already had.
- Every field keeps its default; no file at all keeps working.
- The old file names and the old flat shape keep working with a
  one-line deprecation notice. Nothing ever errors merely for being in
  the old shape.
- mf-run and the worker have no machine config and gain none.
