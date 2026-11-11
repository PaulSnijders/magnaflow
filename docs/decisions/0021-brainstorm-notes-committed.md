---
date: 2026-11-11
topic: mf-spec, spec-kit, brainstorm, scratch
status: accepted
---

# 0021 — Brainstorm notes are committed, but stay out of the record

## Situation

Since the brainstorm skill was added, its notes lived in
`.scratch/brainstorm/` and `.scratch/` was in `.gitignore`: temporary
thinking, never part of the record. In practice the notes outlive the
session that wrote them. `/architect` reads them, decision 0019 was
built on one, and after a few months this repo held six. Then a
machine change came up: a git-ignored folder exists on one disk only,
and a new laptop would start without it. Nobody else could read the
thinking behind a decision either, not even the parts that were
deliberately rejected.

## Options

1. **Keep them git-ignored**, and copy the folder by hand when changing
   machines.
2. **Commit them as records** in `docs/` (for example
   `docs/brainstorm/`).
3. **Commit them where they are**, and move `.scratch/` from
   `.gitignore` to `.ignore`.

## Measurement

- **Survives a lost machine** without a habit someone has to remember.
- **Stays outside the record.** A note is full of half ideas and
  premises that were later dropped. Nobody keeps it true, so it must
  not read as if it were current, either to a person browsing `docs/`
  or to an agent searching the repo.
- **Costs nothing in agent context.** Ripgrep, and with it Claude
  Code's search, skips git-ignored files. That is what kept old
  directions from showing up in every search, and it was the real
  benefit of option 1.
- **KISS**: plain text and git, no new place to sync.

## Choice

Option 3. Option 1 fails the first criterion; option 2 fails the
second, since anything in `docs/` reads as maintained.

Ripgrep also honours an `.ignore` file, even for tracked files. With
`.scratch/` listed there, a default search skips the notes. A search
by path (`.scratch/brainstorm/`, which is what the brainstorm and
architect skills do) still finds them. So the notes travel with the
repo and stay readable on GitHub, while they remain as invisible to a
default agent search as before. The rest is unchanged: the skill does
not commit, a spec never links into `.scratch/` (the lint checks
this), and a decision still restates the premises it relies on
instead of citing the note.

The cost: a note becomes public when its repo is public. The skill now
says so, and the kit's update step asks the user to read the notes
that newly show as untracked before committing them. Kit 1.6.
