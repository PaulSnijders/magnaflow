---
title: "spec-kit 1.0: one kit for private and work projects"
cmd: 0016-cmd-spec-kit-v1-consolidation.md
done: 2026-09-07
summary: "Kit is now 1.0 and tool-neutral: lint from v1 (surface-aware, CRLF-tolerant), .gitattributes and an optional MagnaFlow question in adopt/update, /decision + /spec-fold-in + TEMPLATEs gone, STATUS freshness is a 14-day date, README 347 to 219 lines. Not committed. Next: trial adoption on project-b."
---

## What was done

All eight tasks, in `tools/mf-spec/spec-kit/` unless noted.

1. **`scripts/spec_lint.mjs`** taken over from `docs/mf-spec/v1-kit/`.
   Reads the surface names from `docs/specs/config.yml` (a 15-line
   shape-specific reader, no YAML dependency) and lints
   `docs/specs/<surface>/**`, `concepts/` and `_overview.md`. Accepts
   both spec forms: title + non-blank help + `# Technical`, or
   `# Technical` on line 1 as the only h1. A title with an empty help
   block is a finding with a one-line fix hint. Wired into
   `/spec-drift` step 9 (stdout is the "Format problems" body, verbatim)
   and into `0001-adopt` step 2 as `scripts/spec_lint.mjs`.
2. **`.gitattributes`** is step 4 of `0001-adopt`: the three `eol=lf`
   lines, the "do not normalise CRLF source code" warning, and the
   clean-tree re-checkout recipe from v1. `0002-update` adds missing
   lines only.
3. **MagnaFlow is one question** (`0001` step 5, `0002` step 6): worker
   config and the three ignore lines run only after a yes.
4. **Neutral names.** Kit name is `spec-kit` (version stamp
   `<!-- spec-kit version: 1.0 … -->`); bookkeeping prefix is `lane:`
   (the README already calls `docs/prompts/` the delta lane, so the
   prefix names the thing it marks). "The cockpit renders this" is gone
   from all three places; "MagnaFlow state-spec system" is gone from
   the adopt prompt. Grep of the kit for cockpit/mf:/magnaflow finds
   nothing outside KIT.md and the two prompts' optional step.
5. **Pruned**: `commands/decision.md`, `commands/spec-fold-in.md`,
   both `TEMPLATE.md` files. Genre READMEs are now 28 and 24 lines in
   the v1 format (own rule only; `ls` is the index). Conventions README
   went from 347 to **219** lines, not ~150 — see decisions below. The
   motivations moved to `tools/mf-spec/conventions-rationale.md`.
6. **Freshness**: STATUS.md has no frontmatter; `Generated: <date>`
   only. `/spec-drift` step 0 treats older than 14 days, or missing, as
   the first line of the summary with the days overdue. The 14-day
   term is stated once, in the README's STATUS.md section. ACCEPTED.md
   and the acceptance step are gone from `/spec-drift` and the skill.
7. **KIT.md** rewritten for 1.0: contents table, the v1 "Deliberately
   not in the kit" table extended with the docs CI gate, `/decision`,
   `/spec-fold-in`, the genre TEMPLATEs, the audit-state frontmatter +
   ACCEPTED.md, and kit layers. Migration log 0.1–0.14 removed.
8. **`0002-update`** rewritten as seven idempotent steps plus a "From
   0.x to 1.0" section: place lint and `.gitattributes` if missing,
   remove the two retired commands, leave STATUS.md frontmatter and
   ACCEPTED.md alone (the summary tells the user its entries are back
   on the table).

Also updated, outside the kit: `tools/mf-spec/README.md` and
`system.md` no longer describe ACCEPTED.md, the audit frontmatter or
`/spec-fold-in`, and describe the lint as current rather than planned.

## Verification

- Lint against **wozzol2** (2 surfaces, 10 `# Technical`-first specs)
  and **project-a** (2 surfaces): every spec of either form passes. What
  remains are real content findings in those repos, not lint faults:
  wozzol2 `_overview.md` has a title and no `# Technical`; six
  `design.md#website-surface` anchors and one
  `concepts/context-images.md#…` anchor do not resolve; four `Code:`
  paths point into an external framework repo; project-a `leerinhoud.md`
  lists `Code:` paths without their `web/` prefix.
- A synthetic fixture with three declared surfaces, a stray `pages/`
  folder, a CRLF file, a wrapped `Code:` path, a `Why:` line and a
  `DRAFT:` line produced exactly the five intended findings: empty
  help, `# Technical` not on line 1, undeclared folder, declared surface
  without folder, missing `Code:` path. The valid files were silent.
- `0002-update` idempotency is by construction (every step is "if
  missing" or "overwrite with the same file"); it was not executed
  against a repo — that is the project-b trial.

## Decisions taken while implementing

- **The lint normalises CRLF on read.** The cmd's reason for
  `.gitattributes` was that the lint fails on CRLF checkouts; the first
  run on wozzol2 (`core.autocrlf=true`, no `.gitattributes`) reported
  every CRLF file as "0 h1". A lint that fails on line endings fails
  for the wrong reason, and the fix is one `replace`. The
  `.gitattributes` step stays, with its reason restated honestly
  (cross-machine diff noise, not the lint). This repo shows the same
  symptom: git warns "LF will be replaced by CRLF" on every file
  touched today, because magnaflow itself has no `.gitattributes`
  (out of scope per the cmd, but worth a one-line commit).
- **Lint takes an optional repo-root argument** so it can be run
  against another checkout without installing it there — how the
  verification above was done.
- **Two extra lint checks** beyond v1: a `docs/specs/` folder that is
  not a declared surface, and a declared surface without a folder. The
  first is exactly the project-b `pages/` → `<surface>/`
  migration signal; `0001-adopt` step 1 now says what to do when it
  fires.
- **`Code:` parsing is tolerant**: paths may be bare or backticked,
  wrapped after a `/`, followed by `(symbols)`; URL routes, globs and
  `{placeholders}` are skipped; `Why:` and `DRAFT:` may follow the
  `Code:` paragraph. All of these occur in wozzol2 today.
- **README at 219 lines, not ~150.** Everything below 219 meant
  cutting a rule or an example (the cmd frontmatter block, the status
  list, the branch rule). The 128 lines removed were the motivations
  and the ACCEPTED/frontmatter section; the rules are all still there.
  If ~150 is a hard target, the candidates are the prompt-lane section
  (moving statuses and frontmatter to a `docs/prompts/README.md`) —
  that is a structural choice, not a trim, so it is left to you.
- **`_overview.md` is linted like a spec** (as in v1). Wozzol2's fails
  on that; project-a's passes. If `_overview.md` is meant to be free-form,
  it is one line in `NON_SPEC_FILES`.
- **Kit README example `file.md#rounding-rules`** — the conventions
  README and `docs/CLAUDE.md` are excluded from the anchor-reference
  check because their examples are illustrations, not references.

## Not done / open

- Not committed (per the global rule). The `docs/mf-spec/v1-kit/`
  copy is still untracked, as the cmd asked.
- Tooling still writes nothing with the `lane:` prefix: neither the
  worker controller nor the cockpit hard-codes `mf:` (grep of `tools/`
  finds no commit-prefix usage), so the rename is documentation-only.
- The rejected docs CI workflow (`v1-kit/workflows/docs.yml`) was not
  carried over, as specified.
