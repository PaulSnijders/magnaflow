---
title: Document follow-up command naming (NNNNB/C) in the spec-kit
status: done
specs:
  - docs/mf-spec/system.md
attempts: 0
max_attempts: 3
created: 2026-07-13
---

## Goal

The installable spec-kit (`tools/mf-spec/spec-kit/`) documents the
prompt-lane follow-up naming convention, so target repos learn it the
same way the magnaflow repo itself now records it in
`docs/mf-spec/system.md`: a follow-up to `0005-name` is `0005B-…`,
then `0005C-…` (parent implicitly A, uppercase suffix). The suffix is
human-facing grouping/sorting only; session continuity comes from
`resume:` in the cmd frontmatter, never from parsing the name.

## Context

Doc-only change, no code. First check whether the kit's conventions
README (the file with the version stamp) mentions the `NNNN` lane
naming at all:

- If it does: add the follow-up note there (one short paragraph,
  mirroring the wording in `docs/mf-spec/system.md`'s
  `docs/prompts/` comment), bump the version stamp, and add a
  per-version migration note to `KIT.md` so update prompt 0002
  applies it idempotently to already-adopted repos.
- If it does not mention lane naming: change nothing, and report
  that in the rst so we know the kit was checked and is clean.

## Acceptance criteria

- [ ] Kit conventions README either documents the follow-up naming
      or was confirmed not to document lane naming at all.
- [ ] If changed: version stamp bumped and KIT.md migration note
      added, consistent with the kit's existing versioning style.
- [ ] No changes outside `tools/mf-spec/spec-kit/`.
