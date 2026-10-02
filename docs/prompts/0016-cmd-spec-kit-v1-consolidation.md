---
title: "mf-spec kit 1.0: one kit for private and work projects"
status: done
created: 2026-09-07
---

## Context

Two variants of this spec system exist: this kit
(`tools/mf-spec/spec-kit/`, version 0.14) and an older v1 line that runs
on work projects. They are siblings — same genres, same `# Technical`
split, same `/spec` + `/spec-drift` — but they have drifted apart. With a
handful of projects, merging is cheap now; in a year it will not be.

**Decision: this kit is the kit.** No merge of equal variants and no
core/optional layering. From v1 only the three things that are missing
here are taken over; the rest of this work is pruning.

The v1 source is kept for reference in `docs/mf-spec/v1-kit/` (copy of
the current work version, 2026-09-07). Read `KIT.md` and `README.md`
there.

Why no layers: the magnaflow coupling is ~20 lines in the adopt prompt
(step 3b/3c) plus five word choices. An `optional/` structure around that
is more machinery than what it isolates. The prompt lane is a file
convention — copying and pasting a cmd works without any tooling, so the
runner is already optional by nature.

## Task

**1. Take over `spec_lint.mjs`** into `spec-kit/scripts/spec_lint.mjs`,
from `docs/mf-spec/v1-kit/scripts/`. Node, no dependencies. This is the
only real gap: the kit mentions "Format problems" in STATUS.md but ships
no linter. Two adjustments are needed:

- It assumes `docs/specs/pages/`. It must read the surfaces from
  `docs/specs/config.yml`.
- It demands exactly two h1s. A file whose *first* line is
  `# Technical` and that has exactly one h1 is valid (the optional-help
  form from the conventions). A title with an empty help block stays an
  error — that is precisely the case the lint must catch.

Wire it into `/spec-drift` as the source of the "Format problems"
section (see how v1's `spec-drift.md` does that, step 5c) and into
`0001-adopt` as a file to be placed.

**2. `.gitattributes` in `0001-adopt`.** At minimum `*.md text eol=lf`,
plus `*.mjs` and `*.yml`. With `core.autocrlf=true` — the Windows
default — git checks LF out as CRLF, the heading regex misses
`# Technical` in *every* spec, and the lint rejects everything. It recurs
on every fresh clone, so it belongs in adoption and not in a
troubleshooting note. Also take over the warning *not* to normalize
existing CRLF source code. v1's `0001-adopt` step 5 has the full text
including the `git rm --cached -r . && git reset --hard` recipe.

**3. Make the magnaflow installation conditional.** Step 3b
(`.magnaflow/config.yml`) and 3c (the three gitignore lines) in
`0001-adopt`, and their counterparts in `0002-update`, get one question
in front of them: does this project run with magnaflow? If not, skip.
This is the whole separation between "install the spec system" and
"install magnaflow" — nothing more.

**4. Neutralize names.** The kit will also run on projects that do not
use magnaflow; those currently get instructions about tooling that is
not there. Five places:

- commit prefix `mf:` in `docs/specs/README.md` and `CLAUDE-section.md`
- "the cockpit renders this" (3×, around `summary:` and the STATUS.md
  header)
- "MagnaFlow state-spec system" in `0001-adopt`

Pick a neutral kit name and a neutral bookkeeping prefix. Behaviour does
not change; only the assumption that a cockpit is watching disappears.

**5. Prune.** These crept in as improvements but are not used in
practice:

- `commands/decision.md` and `commands/spec-fold-in.md` — gone. Creating
  one numbered file needs no command; the recipe fits in
  `decisions/README.md`.
- `docs/decisions/TEMPLATE.md` and `docs/context/TEMPLATE.md` — gone. The
  folder README *is* the template.
- Shorten the two genre READMEs to the v1 format (see
  `docs/mf-spec/v1-kit/decisions-README.md` and `context-README.md`): a
  handful of lines holding only the genre's own rule.
- `docs/specs/README.md` from 345 to ~150 lines. The rules stay; the
  motivations ("this exists because the previous version…") move to a
  design note in `docs/mf-spec/`. A conventions file that you re-read
  every session must be short.

**6. Simplify STATUS.md freshness.** Only the line `Generated: <date>`
stays. Gone: the frontmatter `audited_at`, `unaudited_commits` and
`freshness`, plus `ACCEPTED.md` — the latter hung off the sha bookkeeping
and without a sha has nothing to expire against.

`/spec-drift` step 0: if `generated` is older than 14 days, or the line
is missing, that is itself a finding — first line of the summary, with
the number of days overdue. The 14-day term is stated once in the README
as a convention, not as a derived value in every file.

Reason: an audit goes stale through code movement, not through time, but
reacting to an expired date is cheap — on a quiet project `/spec-drift`
comes back green in seconds. That is a confirmation, not a false alarm. A
dashboard that wants it more precisely counts
`git rev-list --count --since=<date> -- <code roots>` itself.

**7. KIT.md.** Version to **1.0**; the migration log 0.1 → 0.14 is
dropped (history no adopter needs). Take over the table **"Deliberately
not in the kit"** from v1's `KIT.md`, with the rejected ideas and their
reasons, so they do not creep back in. Add: `/decision`,
`/spec-fold-in`, the genre TEMPLATEs, and the CI gate
`workflows/docs.yml` (present in v1 but not coming here — it only pays
off for a team with a PR flow; the lint runs locally via `/spec-drift`).

**8. `0002-update`**: migration note 0.14 → 1.0. Repairs are idempotent —
place `spec_lint.mjs` and `.gitattributes` if missing, remove the deleted
commands from `.claude/commands/`, and leave the STATUS.md frontmatter as
it is (the next `/spec-drift` writes the new form). Specs, `config.yml`,
`docs/prompts/`, `docs/decisions/` and `docs/context/` stay untouched.

## Verify

- Run `spec_lint.mjs` against a repo with multiple surfaces *and* against
  a spec that starts with `# Technical`. Both must come back clean; a
  title with an empty help block must fail. (`C:\GIT\wozzol2` or
  `C:\GIT\project-a`)
- Read `docs/specs/README.md` as someone who does not know the system: is
  there still a line that refers to magnaflow tooling?
- Running `0002-update` twice changes nothing the second time.

## Not in scope

- This repo does not adopt the kit on itself; nothing is created in
  magnaflow's own `docs/`.
- The trial adoption on a real project. That is a separate prompt in that
  project, and the first one must be **project-b**: the layout migration
  `docs/specs/pages/` → `docs/specs/<surface>/` has to happen there
  anyway, so that is where it shows whether the adopt prompt runs through
  without manual work. A clean project proves nothing.
- Cleaning up `docs/mf-spec/v1-kit/` — leave it until the trial adoption
  is done.
