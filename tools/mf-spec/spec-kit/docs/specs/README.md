# Specs — conventions

<!-- spec-kit version: 1.3 — do not edit; used by the update prompt -->

One markdown file per page, grouped per surface. The file path is the
identity, git history is the timestamp. No frontmatter.

## Five genres, five houses

| Genre | Folder | Answers | Life |
|---|---|---|---|
| Page spec | `docs/specs/<surface>/` | What does this screen do now? | mutates |
| Concept | `docs/specs/concepts/` | How does this mechanism work now? | mutates |
| Decision | `docs/decisions/` | Why did it become this, what was weighed? | frozen |
| Context | `docs/context/` | What did the outside world say? | maintained |
| Prompt/run | `docs/prompts/` | What was ordered, what came out? | frozen |

**Specs mutate, decisions and prompts do not.** A spec is updated when
behavior changes; a decision that no longer holds gets a successor, never
a rewrite (correcting a moved link, wrong data or something that should
never have been committed is fine). **Context is managed**: one file per
conversation, follow-ups added to it, mistakes corrected and noise
removed — but never what someone said (see its README). Pick a house
before you write — nothing lives in `docs/` root. The record folders
carry their own README and may stay empty forever. The rest of this file
covers the two spec genres.

## The one rule

**Any change that affects behavior updates the affected spec in the same
turn and the same commit.** Specs lead (spec ahead = a work order) or
change together with the code — never behind. A `hotfix:` commit prefix
is the escape hatch, paid off by folding the change into the spec
afterwards (a debt line in STATUS.md until then).

`/spec-drift` is therefore an audit, not maintenance: it should always be
green. A finding means the enforcement has a gap — fix the gap, not just
the spec.

## Layout

```text
docs/specs/
  README.md          # this file
  config.yml         # surface declarations (see file)
  STATUS.md          # generated exception report — never edit by hand
  _overview.md       # the application as a whole
  concepts/          # cross-cutting topic specs, incl. design.md
  <surface>/         # one folder per surface in config.yml,
                     # mirroring that surface's routes
```

A project with one surface still uses its surface folder. Route-to-path
mirroring follows the `routes` glob and `slug` rule in config.yml, e.g.
`app/orders/[id]/page.tsx` → `docs/specs/app/orders/[id].md`. The only
non-page file allowed inside a surface folder is `<group>/_group.md`:
shared explanation for a group of pages, created only when the group
genuinely needs it; page specs never repeat what it says.

## File format

```markdown
# <Page title>

<Help section: plain language for end users.>

# Technical

<Notes for developers/admins.>
```

`# Technical` is the split marker: above it is end-user help (shown
in-app when the surface has `help: true`), below it is admin/developer
content. Use `##` and deeper inside sections. Help is written in the
surface's `help_language` (default English); translations are generated,
never hand-edited. `# Technical` is always English.

**Help is optional, but its absence must be explicit.** Two valid forms,
for pages and concepts alike:

- *With help*: exactly two h1s — title, help text, `# Technical`.
- *Without help* (admin pages, technical concepts): the file's first line
  is `# Technical`, and it is the only h1. No title — the filename is
  the identity.

A title followed by an empty help section is a format error; the lint
rejects it. Rendering is fail-closed: users see help only for the full
two-h1 form.

## Concepts

**A mechanism that determines behavior and cannot be read off one module
gets a concept — even if only one page shows it.** Pricing rules,
permissions, an algorithm, a state machine: one file in `concepts/`, same
format. Page specs link a concept in one line and explain nothing more.

Concepts have no route; each `# Technical` section ends with a `Code:`
line naming the owning modules as repo-relative paths (that is how drift
is tracked), optionally followed by a `Why:` line pointing at the
decision the mechanism came out of. The concept carries the outcome and
this one line, never the argument; the decision carries the argument,
never the current truth. `Why:` belongs on a page spec too when a
decision is about one page.

```markdown
Code: src/billing/Invoice.cs, src/billing/Rounding.cs
Why: decisions/0012-invoice-rounding.md
```

**`concepts/design.md` always exists**: the design system reference,
in two halves — **engineering principles** (layering, dependency policy,
security basics, reuse before new code; `/architect` checks every cmd
against them) and, when there is a UI, **appearance**. Page specs never
describe appearance in prose; they reference this file and record only
deviations. A short `Quality baseline` section records which mechanical
checks (warnings as errors, analyzers, linter, vulnerability scan) are
on and which were only advised.

## Writing rules

- Specify **decisions, not defaults**: "if the implementer chooses
  differently here, do I care?" If not, leave it out. Unspecified = may
  vary, so do pin what must stay stable even when obvious: URLs, data
  formats, user-facing terminology.
- Keep it short. Help sections aim for under 300 words, Technical
  sections for under ~800. Technical sections record what the code
  cannot say — decisions and reasons, quirks, edge cases, one-line data
  contracts — and never retell it: no field, column or endpoint lists
  the code already shows (a schema concept names the entities and
  records only what the code does not enforce or explain). Every word
  in a spec is one the next behavior change has to keep true.
- A spec never depends on scratch: no links into `scratch/`, `temp/`
  or `tmp/` folders. What a spec relies on is promoted into a spec or a
  record first; `spec_lint` reports such links.
- Refer to code by symbol name, never line numbers; to docs by anchor
  slug (`file.md#rounding-rules`), never section numbers.
- `BUG:` prefix for known bugs; say "standard CRUD, nothing special" for
  deliberate openness so nobody keeps probing it.

## Completeness

Complete means: every route enumerated by a surface's glob has a spec
file; every cross-page behavior has a concept; `design.md` and
`_overview.md` exist. `/spec-drift` reports gaps; `/spec` writes a
one-time draft from code for anything missing — after that, the
spec-first rule applies. Complete does NOT mean the specs mirror the
code: internals are deliberately absent. Spec-first is a property of
**changes**, not of the codebase; two rules manage thin coverage:

- **Deepen on touch**: when a change lands in an area whose spec is too
  thin to review it against, bring the spec up to level first, same turn.
- **`DRAFT:` marker**: machine-generated specs end their `# Technical`
  section with `DRAFT: generated from code, not human-reviewed.` Remove
  it the first time a human genuinely works with the spec.

## Prompts — the delta lane (`docs/prompts/`)

Medium-tier changes travel as numbered prompt pairs, inside the docs
plane so a design session can write commands and read results without
seeing code. Small changes need no pair; exceptional ones get their own
heavier process.

```text
docs/prompts/
  0001-cmd-short-name.md   # the command (human's intent): goal, decisions, tasks
  0001-rst-short-name.md   # the result (executor's report)
  0001-qa-short-name.md    # only when questions arise: the shared dialogue
```

**Numbering**: read the next number off disk every time — highest number
present in `docs/prompts/` + 1 — never from what the conversation
remembers. A follow-up reuses the parent's number with an uppercase
suffix (`0005-name` → `0005B-name`); the suffix is grouping only, session
continuity comes from `resume:`.

`cmd-` frontmatter — the only source of truth for status:

```markdown
---
status: draft   # draft | ready | running | questions | done | aborted
created: 2026-07-07
branch: feat/0007-short-name  # optional: do the work on this branch
base: main                    # optional: base for branch (requires branch:)
resume: 0005-short-name       # optional: continue that command's session
specs: [docs/specs/app/orders.md]   # optional: state specs this touches
---
```

`draft` — being shaped, do not pick up. `ready` — may be picked up
(by the worker, or by hand with `/cmd-go`).
`running` — picked up; the executor creates/updates the `rst-` file.
`questions` — paused, questions in the `qa-` file; answer beneath each
and set the cmd back to `ready` (rounds append). `done` — implemented,
`rst-` written, owning specs updated in the same change. `aborted` —
deliberately stopped, reason in the `rst-` file.

`rst-` frontmatter: `title`, `cmd` (the cmd filename), `done` (date) and
`summary:` — one line of plain text (quote it if it contains a colon)
answering "what changed, and do I need to do anything?". The body is the
report the *next* conversation reads: what was done, decisions taken
while implementing, anything skipped or uncertain, and the questions the
cmd left open that the executor answered for itself — a long list of
those means the command was under-specified.

There is no central status file and no generated list of commands:
state lives in the frontmatter, and the lane itself is the overview
(`grep -l "status: ready" docs/prompts/*-cmd-*.md`, or the cockpit).

### Branches

Default is **no branch**: spec + code in the same commit on the current
branch. Set `branch:` only when the change earns it — it spans sessions,
parallel work matters, or you want a review gate. Then **bookkeeping on
the invoking branch, work on the work branch**: cmd/rst files and
statuses commit on the branch the executor was started from, code and
its spec updates travel in the work branch (created from `base:`,
default main/master). The executor pushes both when a remote exists and
does not open a pull request — opening it and merging are the human's
call. Never update prompt statuses on a checked-out work
branch.

Bookkeeping commits (status transitions, rst/qa updates without code)
use the prefix `lane:` and include `[skip ci]`, so
`git log --invert-grep --grep="^lane:"` shows only real work.

## Quality pass

When: per pull request; when the repo works on `main` without PRs,
about monthly over the commits since the last pass. Run
`/security-review` and `/code-review` (built-in Claude Code skills) on
the change; on `main`, `/code-review` on the range. Triage every
finding — real or not, one line why — together with a senior where
there is one. Record it in `docs/context/YYYY-MM-DD-quality-pass.md`:
the range reviewed, the findings with their verdict; short. Real
findings become cmds via `/architect` in the same conversation, never
automatically. Habit: `/simplify` on your own diff before opening a PR.

## STATUS.md

Generated by `/spec-drift`, updated by `/spec`; never edit by hand. An
exception report: missing specs, specs without a page, stale specs (code
ahead — an enforcement gap), spec ahead (normal: work orders), hotfix
debt, format problems (from `scripts/spec_lint.mjs`),
recent updates. All sections read "none" in steady state.

The "Format problems" section is the lint's stdout verbatim, so the lint
does not scan STATUS.md — a reported broken anchor must not come back as
a second, uncleanable problem on the next run.

Its `Generated: <date>` line is the freshness mechanism: **older than 14
days, or missing, is itself a finding** on the next `/spec-drift`. A
quiet project comes back green in seconds — a confirmation, not a false
alarm.

Below it, `Quality pass: <date> (<N> days ago)` names the newest
`docs/context/*-quality-pass.md` (see [Quality pass](#quality-pass)), or
reads `Quality pass: none`. Older than 30 days is a finding; `none` is
not, so a fresh adoption does not nag.
