---
title: Document follow-up command naming (NNNNB/C) in the spec-kit
cmd: 0002-cmd-spec-kit-followup-naming.md
done: 2026-07-13
---

## What was done

Checked `tools/mf-spec/spec-kit/docs/specs/README.md` (the version-
stamped conventions README) — its "Prompts — the delta lane" section
covered numbered cmd/rst/qa pairs but said nothing about follow-up
naming. Added one short paragraph mirroring `tools/mf-spec/system.md`'s
`docs/prompts/` comment, right after "Number first so a pair sorts
adjacently in any file explorer.":

> A follow-up to an existing prompt reuses its number with an
> uppercase suffix — `0005-name` → `0005B-name`, then `0005C-name`
> (the parent is implicitly A). The suffix is human-facing
> grouping/sorting only; session continuity comes from `resume:` in
> the cmd frontmatter, never from parsing the name.

Bumped the version stamp `0.7` → `0.8` in that file, and added the
`0.7 → 0.8` migration note to `KIT.md` (documentation-only, no repair
needed — `0002-update-spec-system.md` already overwrites
`docs/specs/README.md` wholesale on update, so the new wording lands
automatically and idempotently).

## Decisions

- No changes to `0002-update-spec-system.md` or `KIT.md`'s "Overwrite"
  list: `docs/specs/README.md` is already a whole-file overwrite
  target, so a doc-only wording change needs no special repair step —
  only the migration-note entry for the version history.
- Left `docs/CLAUDE.md`'s existing `resume:`/`group:` mention as is;
  it already assumes the naming convention documented in README.md,
  which is now consistent.

## Scope

Only `tools/mf-spec/spec-kit/docs/specs/README.md` and
`tools/mf-spec/spec-kit/KIT.md` were touched, both inside
`tools/mf-spec/spec-kit/`.
