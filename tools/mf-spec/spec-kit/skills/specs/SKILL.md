---
name: specs
description: State specs for this project (docs/specs/). Use when changing any behavior, creating or updating specs, or when the user asks about page documentation, in-app help, or spec drift.
---

# Specs (code side)

Format, naming, and writing rules are defined in `docs/specs/README.md`.
Read that file first; it is the single source of truth.

This skill adds the code-side rules:

## Spec-first (the hard rule)

Any change that affects behavior updates the affected spec in the same
turn and the same commit — including small follow-up remarks ("make that
button blue" changes `design.md` or the page spec's deviation note).
When you implement something, find the owning spec via
`docs/specs/config.yml` (surface globs) or a concept's `Code:` line, and
update it before you commit. If the user explicitly declares a hotfix,
prefix the commit `hotfix:` and add a debt line to STATUS.md.

**Deepen on touch**: if the owning spec is too thin to review your
change against (e.g. a machine-generated draft), bring it up to level
first — same turn — and remove its `DRAFT:` marker. Do not deepen specs
in areas you are not touching.

## Grounding

When writing or updating a spec, read the actual code first: resolve the
route via the surface's `root` and `routes` in config.yml, plus the
components/endpoints it uses. Describe what the code actually does.

If code and an existing spec disagree, do not silently overwrite the
spec: spec ahead is the normal spec-first state. Report the mismatch and
ask which side is the truth, unless the user already made that clear.

## Dates

Always use git for timestamps, never file mtimes:
`git log -1 --format=%cs -- <path>`.

## STATUS.md

After changing specs or running checks, keep `docs/specs/STATUS.md` in
sync as described in the README. It is an exception report; steady state
is "none" everywhere with a `Generated:` date less than 14 days old.
Never edit STATUS.md by hand — a finding you consider fine is fixed at
its enforcement gap, not waved through.
