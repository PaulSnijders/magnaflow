---
title: "mf-spec kit: decisions/ and context/ as first-class genres"
status: done
created: 2026-09-02
---

## Context

The kit has two genres that mutate (page specs, concepts) and one that
is frozen (the prompt lane). It has no home for *why something was
decided* or for *what the outside world said*. In the v1 evaluation
that gap pushed `docs/` root from 8 to 23 files; in this repo the
decision genre exists unnamed and unnumbered across `docs/fase*/`.

Design: `docs/decisions/0016-genres-and-gate.md`, section "Five
genres, five houses". Read it first — it carries the reasoning for
every choice below.

This changes the **kit** (`tools/mf-spec/spec-kit/`) only. MagnaFlow
has not adopted the kit on itself, so no folders are created in this
repo's own `docs/`.

## Task

**1. Two new genre folders in the kit**, mirroring how `docs/specs/`
is shipped (`spec-kit/docs/<x>/` → `docs/<x>/` in a target repo):

- `spec-kit/docs/decisions/README.md` + `TEMPLATE.md`
- `spec-kit/docs/context/README.md` + `TEMPLATE.md`

Each README states what belongs there, what does not, the naming rule
and the index convention — that README is what makes an empty folder
an invitation instead of clutter, so write it for a reader who has
never seen the system. Both folders are created by adopt and may stay
empty forever; say so in the README.

`decisions/` is numbered `NNNN-slug.md`: a decision is cited and
superseded, and "0021 supersedes 0012" is a sentence that works with
numbers and not with dates. Same numbering rule as the prompt lane —
read the highest number off disk, never from what a conversation
remembers. Frontmatter (YAML, like the prompt lane, unlike specs —
records carry metadata, specs do not): `date`, `topic` (comma
separated, one file may carry several), `status: accepted |
superseded`, and `supersedes:` when it replaces one. Sections:
Situation → Options → Measurement → Choice.

`context/` is ISO-dated `YYYY-MM-DD-slug.md`: context has no identity
you cite, you search it chronologically or by keyword. Frontmatter
`date`, `topic`, `source` (who or what it came from). State the ISO
rule explicitly in the README — without it `05082026_naam.md` appears
by itself and sorts by day-of-month.

Both: slug 2–5 words, subject before form (`george-data-request`, not
`email-to-george-about-the-data`). No subfolders — `Topic:` and grep
replace them and carry two topics where a folder carries one. Split
only above ~50 files, and then by year in `context/` only;
`decisions/` never splits.

**2. `/decision <slug>` command** (`spec-kit/commands/decision.md`):
read the next number off disk, fill the template, add the index line
to `decisions/README.md` (newest on top), and add the `Why:` line to
the concept it belongs to when there is one. Ask for the concept if it
is not obvious rather than guessing.

**3. Conventions README** (`spec-kit/docs/specs/README.md`): the genre
table near the top, with the rule that holds it together —

> Specs mutate, records do not. You update a spec when behavior
> changes. You never update a decision, context item or prompt — you
> write a new one that supersedes it.

Plus the concept/decision split: a concept carries the outcome and one
line `Why: decisions/0012`, never the argument; a decision carries the
argument, never the current truth. Add `Why:` to the Concepts section
next to the existing `Code:` line — same shape, same place, so it
costs no new convention.

**4. Wiring**: `0001-adopt` creates both folders with their READMEs;
`0002-update` creates them if missing and never touches their content
(add both to its NEVER-touch list). One line each in
`spec-kit/docs/CLAUDE.md` (a docs-only session is where decisions and
context are written) and `spec-kit/CLAUDE-section.md`.

**5. Version**: bump the stamp in `spec-kit/docs/specs/README.md` and
`KIT.md` to 0.13 with a migration note — the repair is "create the two
folders with their READMEs if missing", idempotent.

## Verify

Re-read the two READMEs as someone who has never seen the system: is
it clear what goes in, what stays out, and how to name it? Check that
`0002-update` run twice would change nothing the second time.

## Not in scope

The gate (`spec_lint`, worker hook, cockpit signal), commit-message
rules, the `docs/` root allowlist, `HANDOVER.md`, and migrating this
repo's own `docs/fase*/` material into the new genres. All of those
are separate; the last one is not a kit change at all.
