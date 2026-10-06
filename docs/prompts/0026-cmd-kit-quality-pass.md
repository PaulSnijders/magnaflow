---
title: "spec-kit 1.2: engineering principles in design.md, quality baseline question, quality pass"
status: ready
created: 2026-10-06
---

## Context

Kit-text only — no tool code. Design and the options weighed:
`docs/decisions/0017-quality-pass.md`. In short: in team projects the
kit should steer code quality through what it already has (design.md as
the guide `/architect` checks every cmd against, the lane, the context
genre, `/spec-drift`) instead of a new tool.

Read first: `tools/mf-spec/spec-kit/0001-adopt-spec-system.md`,
`0002-update-spec-system.md`, `CLAUDE-section.md`, `KIT.md`,
`commands/spec-drift.md`, `docs/context/README.md` (all under
`tools/mf-spec/spec-kit/`), and `tools/mf-spec/system.md` +
`tools/mf-spec/README.md`.

Today `0001` step 6 describes design.md as "the design system: layout
rules, components, tone" — UI only. MagnaFlow's own
`docs/specs/concepts/design.md` shows the missing half (principles,
technology, "new dependencies need a concrete need").

## Task

All paths under `tools/mf-spec/spec-kit/` unless stated otherwise.

1. **design.md carries engineering principles** (`0001` step 6). Reword
   the design.md bullet: the design system has two halves —
   **engineering principles** (architecture/layering, dependency policy,
   security basics such as secrets never in code and parameterised
   queries, reuse before writing new code) and, when there is a UI,
   **appearance** (layout rules, components, tone). Derive the
   principles the code already follows, show them to the user, and let
   them confirm or adjust — a handful of short rules, not a handbook.
   Add one sentence why: `/architect` checks every cmd against
   design.md, so a principle written there steers every change.

2. **New step in `0001`: quality baseline** (after step 5, before the
   spec sync; renumber the following steps and fix any step references,
   including the one in `0002`). Stack-neutral wording, with examples:
   - Check what the repo already has: warnings as errors and analyzers
     (.NET: `TreatWarningsAsErrors`, `AnalysisLevel`, ideally in
     `Directory.Build.props`), a linter (Angular/JS: eslint), and a
     dependency vulnerability scan in CI (`dotnet list package
     --vulnerable`, `npm audit`).
   - Ask the user one question: **set it up, or advise only?** The user
     decides.
   - Set up: only what keeps the build green today. If switching it on
     would turn the build red, do not fix the warnings here — leave it
     off, note the count, and suggest a separate cmd.
   - Either way, record the outcome in design.md in a short
     "Quality baseline" section (what is on, what was advised), so the
     question is asked once and `0002` can see it was answered.
   - Projects without code yet: record the advice only.

3. **`0002`: the same two items for existing installs**, in a new
   "From 1.1 to 1.2" section. If design.md has no engineering
   principles, or no Quality baseline section, ask the user whether to
   add it (as in `0001` step 6 / the new baseline step) — adding a
   section only, never rewriting existing text. Extend step 7 ("NEVER
   touch") with this one exception, worded like the existing context
   README exception.

4. **Quality pass — a short section in `CLAUDE-section.md`** (heading
   "Quality pass", about ten lines):
   - When: per pull request; when the repo works on `main` without PRs,
     about monthly over the commits since the last pass.
   - What: run `/security-review` and `/code-review` (built-in Claude
     Code skills) on the change; on `main`, `/code-review` on the range.
   - Triage: per finding, real or not, one line why — together with a
     senior where there is one.
   - Record: `docs/context/YYYY-MM-DD-quality-pass.md` — range reviewed,
     findings with their verdict. Short.
   - Real findings become cmds via `/architect` in the same
     conversation. Never automatically.
   - Habit: `/simplify` on your own diff before opening a PR.
   Also add one bullet to `docs/context/README.md`: a quality-pass
   record is filed here as `YYYY-MM-DD-quality-pass.md`.

5. **`/spec-drift` shows the last pass** (`commands/spec-drift.md`):
   - New step (next to step 0): find the newest
     `docs/context/*-quality-pass.md` by its filename date.
   - STATUS.md gets one line under `Generated:`:
     `Quality pass: <date> (<N> days ago)` or `Quality pass: none`.
     Update the structure block in step 9.
   - Older than 30 days is a finding: report it in the summary like the
     expired `Generated:` date, no section of its own. `none` is **not**
     a finding.
   - Check `scripts/spec_lint.mjs` still accepts STATUS.md (it skips
     it — confirm, change nothing if so).

6. **Version 1.2.** Bump the stamp in `docs/specs/README.md` (kit copy)
   and the version in `0001`'s first paragraph, `KIT.md` (title and a
   short 1.2 paragraph like the 1.1 one), `tools/mf-spec/system.md`
   (Distribution: current version; add the quality pass and the
   design.md halves where design.md and `/spec-drift` are described)
   and `tools/mf-spec/README.md` where it describes `/spec-drift`. Add
   the dropped `/quality` command to KIT.md's "Deliberately not in the
   kit" table, one line, citing decision 0017.

7. **Run `0002` on this repo** (CLAUDE.md dogfooding rule), so
   `.claude/commands/spec-drift.md`, `docs/specs/README.md`,
   `docs/CLAUDE.md` and the root `CLAUDE.md` section match the master.
   Answers for this repo, decided in advance — do not ask:
   - MagnaFlow tooling: yes (nothing to change).
   - Engineering principles: `docs/specs/concepts/design.md` already has
     them; leave that text alone.
   - Quality baseline: **advise only**. Add a short "Quality baseline"
     section to `docs/specs/concepts/design.md`: today no
     `TreatWarningsAsErrors`/`AnalysisLevel`; advised:
     a `Directory.Build.props` with both, as a separate cmd (state the
     current warning count from a `dotnet build` of each tool solution);
     no CI vulnerability scan (no CI here).
   Then run `/spec-drift`; STATUS.md must stay green and show
   `Quality pass: none`.

## Verify

- `node scripts/spec_lint.mjs` prints `none`.
- `/spec-drift` on this repo: green, `Quality pass: none`, no finding
  for it.
- Diff the installed copies against the master: `.claude/commands/spec-drift.md`
  vs `tools/mf-spec/spec-kit/commands/spec-drift.md`, and the version
  stamp in both `docs/specs/README.md` files reads 1.2.
- Read `0001` top to bottom once as an adopter: step numbers and
  cross-references are consistent.

## Must not change

- No tool code (`tools/*/src`), no new kit command or skill.
- The 14-day `Generated:` rule and the other STATUS.md sections.
- MagnaFlow's existing principles text in `docs/specs/concepts/design.md`.

## Not in scope

- Setting up the strict build in this repo — advise only (decision
  0017); a separate cmd once the warning count is known.
- Architecture tests (NetArchTest/ArchUnitNET) — later, where a layer is
  actually blurring.
- A `/quality` command or quality scores — rejected in 0017.
