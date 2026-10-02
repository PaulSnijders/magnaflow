---
title: "mf-spec kit: decisions/ and context/ as first-class genres"
cmd: 0013-cmd-spec-kit-genres.md
done: 2026-09-02
summary: The kit now ships docs/decisions/ and docs/context/ with a README each, a /decision command, the genre table in the conventions README, and version 0.13. Records default to supersede-don't-rewrite, but may be corrected.
---

## What was done

Kit-only change (`tools/mf-spec/spec-kit/`). MagnaFlow's own `docs/`
was not touched — this repo has not adopted the kit on itself.

**Two new genre folders**, shipped the way `docs/specs/` is shipped:

- `docs/decisions/README.md` + `TEMPLATE.md` — numbered `NNNN-slug.md`,
  frontmatter `date` / `topic` / `status` / `supersedes`, sections
  Situation → Options → Measurement → Choice, index in the README
  (newest on top), number read off disk, no subfolders, never splits.
- `docs/context/README.md` + `TEMPLATE.md` — ISO-dated
  `YYYY-MM-DD-slug.md`, frontmatter `date` / `topic` / `source`,
  free-form body with the load-bearing sentences kept verbatim, same
  index convention, splits by year only above ~50 files.

Both READMEs are written for a reader who has never seen the system:
what belongs, what does not (with the neighbouring genre named for
each), the naming rule with its reason, and the statement that the
folder is created by adopt and may stay empty forever.

**`/decision <slug>`** (`commands/decision.md`): number off disk, fill
the template, handle superseding (the one edit ever made to an existing
decision: flip `status:` and add a pointer), write the index line, add
`Why: decisions/NNNN-slug.md` to the owning concept — and ask which
concept when it is not obvious rather than guessing.

**Conventions README** (`docs/specs/README.md`): "Five genres, five
houses" table right after the intro with the rule *specs mutate,
records do not*, and in the Concepts section the `Why:` line next to
the existing `Code:` line, with the split spelled out — the concept
carries the outcome and the line, the decision carries the argument.

**Wiring**: `0001-adopt` step 2 places both folders; `0002-update`
creates them if missing and lists both as NEVER-touch (a README may
have been adapted to the project, and the files are history). One
paragraph each in `docs/CLAUDE.md` (a docs-only session is where
records are written) and `CLAUDE-section.md`.

**Version 0.13** stamped in `docs/specs/README.md` and `KIT.md`, with
the migration note: create the two folders with README and TEMPLATE if
missing, copy `commands/decision.md`, never touch existing content.

## Decisions taken while implementing

- **"Don't rewrite a record" is a direction, not a prohibition.** The
  first draft said records are never edited; corrected on the user's
  word. Both READMEs now carry an "Editing a record" section: edit
  freely when things moved, when the file carries plainly wrong data
  that would mislead, when something was committed that should not
  have been, or when it was filed mechanically and needs cleaning up —
  a strange sentence in an auto-filed mail is raw input, not scripture.
  Write a new record instead when the *content* changed (a reversed
  choice, a later mail), because rewriting then hides that something
  moved. A more-than-cosmetic correction gets one dated line at the
  bottom. Superseding stays what it is: flip `status:`, add a pointer,
  leave the argument as written.
- **The title stays in the `#` heading**, not in frontmatter — the cmd
  spelled out four frontmatter keys and adding a fifth would have made
  the decision file's metadata heavier than the prompt lane's for no
  reader. The heading repeats the number so a file read out of context
  still identifies itself.
- **Index line shape shown as an example block** in both READMEs
  rather than described in prose, so `/decision` and a human writing
  by hand produce the same line.
- **`context/` says there is no command for it.** Otherwise a reader
  who just met `/decision` waits for a `/context` that does not exist.

## Not done (per the cmd)

The gate (`spec_lint`, worker hook, cockpit signal), commit-message
rules, the `docs/` root allowlist, `HANDOVER.md`, and migrating this
repo's own `docs/fase*/` material into the new genres.

## Verified

- Both READMEs re-read cold: what goes in, what stays out, and the
  naming rule are each stated once and in one place.
- `0002-update` run twice changes nothing the second time: the folders
  are created only when missing, and both are on the NEVER-touch list,
  so a second pass finds them present and does nothing. `commands/*`
  (including `decision.md`) is a plain overwrite of system-owned files
  and is idempotent by construction.
