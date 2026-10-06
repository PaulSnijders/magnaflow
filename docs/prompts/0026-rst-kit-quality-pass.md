---
title: "spec-kit 1.2: engineering principles in design.md, quality baseline question, quality pass"
cmd: 0026-cmd-kit-quality-pass.md
done: 2026-10-06
summary: "Spec kit is now 1.2 (design.md has engineering principles plus a Quality baseline, a quality pass in CLAUDE.md, /spec-drift shows the last pass) and is installed on this repo with STATUS green and Quality pass: none. Nothing to do now; turning on the strict build is a separate cmd, and the analyzer warning counts are in design.md."
---

# 0026 — spec-kit 1.2 quality pass: report

## What changed

All kit text; no tool code was touched.

**Kit master (`tools/mf-spec/spec-kit/`)**
- `0001-adopt-spec-system.md`
  - Version is 1.2.
  - New **step 6, Quality baseline**: check for warnings as errors and analyzers, a linter, and a CI vulnerability scan. Ask one question (set it up, or advise only). Set up only what keeps the build green; otherwise note the warning count and suggest a separate cmd. Record the outcome in `## Quality baseline` in design.md. A project without code records the advice only.
  - The spec sync is now **step 7** and verify is **step 8**.
  - Step 1's no-code path now reads "steps 2–6 … skip step 7".
  - The design.md bullet in step 7 has two halves: engineering principles (derived from the code, confirmed by the user, plus the `/architect` why-sentence) and appearance (when there is a UI). It also says to keep the step-6 section.
  - The closing summary now includes the quality baseline outcome.
- `0002-update-spec-system.md`
  - Step 1 says "every path to 1.2".
  - New "From 1.1 to 1.2" section: if design.md has no principles or no Quality baseline section, ask the user and add the section only.
  - Step 7 gets the design.md exception, worded like the context README one.
  - The existing references to `0001` steps 4 and 5 were already correct, since those steps did not move.
- `CLAUDE-section.md`: new `## Quality pass` section (when, what, triage, record, `/architect`, `/simplify`).
- `docs/context/README.md`: bullet for `YYYY-MM-DD-quality-pass.md`.
- `commands/spec-drift.md`
  - New step `0b`: newest `docs/context/*-quality-pass.md` by filename date. Older than 30 days is a summary finding; `none` is not a finding.
  - The step 9 structure block gets `Quality pass:` under `Generated:`.
- `docs/specs/README.md`
  - Stamp is 1.2.
  - The design.md paragraph names the two halves and the Quality baseline section.
  - The STATUS.md section describes the `Quality pass:` line and its 30-day rule.
- `KIT.md`
  - Title is 1.2; the update line covers 1.1 → 1.2.
  - New 1.2 paragraph.
  - New `/quality` row in "Deliberately not in the kit", citing decision 0017.

**`tools/mf-spec/system.md`**
- The Concepts bullet describes the design.md halves and the Quality baseline.
- New "Quality pass" bullet in "Keeping specs true", with the `/spec-drift` line and the 30-day rule.
- Distribution says 1.2.

**`tools/mf-spec/README.md`**: the `/spec-drift` bullet mentions the quality-pass date and its 30-day rule.

**`0002` run on this repo (dogfooding)**
- Overwrote `docs/specs/README.md`, `.claude/commands/spec.md` and `spec-drift.md`, `.claude/skills/specs/SKILL.md`, `scripts/spec_lint.mjs` and `docs/CLAUDE.md` from the master. `diff -q` shows all of them identical, and both README stamps read 1.2.
- Merged the Quality pass section into the root `CLAUDE.md`. Its spec-system section now matches `CLAUDE-section.md` exactly.
- MagnaFlow config: nothing to change.
- Principles text in `docs/specs/concepts/design.md`: untouched.
- Added `## Quality baseline` to design.md (advise only), just before its `Code:`/`Why:` lines.
- Ran `/spec-drift` by hand and rewrote STATUS.md: `Generated: 2026-10-06`, `Quality pass: none`, all sections `none`, a new Recent-updates line for 0026, and "3 cosmetic diffs".

## Verification
- `node scripts/spec_lint.mjs` prints `none` (0 problems). It still skips `docs/specs/STATUS.md` (`SKIP_REF_FILES`, `NON_SPEC_FILES`), so the script is unchanged.
- `/spec-drift`: green, with `Quality pass: none` and no finding for it.
  - Flagged and judged cosmetic for their concept:
    - `evidence-layout` (64e7c4a only added `.scratch/` to `.gitignore`)
    - `session-resume` (8d3c9ba; its behavior is specced in worker-run/command-lifecycle in the same commit)
    - `watch-supervision` (625e8a7 and a4c5b67 change install.sh's publish/stop order and the self-update timer; the spec for those is `machine-install.md`, updated in those same commits)
  - No hotfix commits.
- I read `0001` once from top to bottom. Steps run 1–8, and every cross-reference (steps 2–6, skip 7, step 6 ↔ step 7, 0002 → 0001 steps 4/5/6/7) is consistent.
- `dotnet build` of all four `.slnx`: succeeded, 0 warnings.

## Warning counts (the input for the follow-up cmd)

At today's settings, all four solutions build with 0 warnings.

With `-p:AnalysisLevel=latest-recommended` (measured on the command line, no file changed), unique warnings are:

| Solution | Warnings |
|---|---|
| mf-cockpit | 321 |
| worker-controller | 253 |
| mf-watch | 29 |
| mf-run | 3 |

Mostly CA1707 (underscores in xUnit test method names: 276 in cockpit, 173 in worker), then CA1051, CA1310, CA1305 and CA1816. A strict-build cmd should decide per rule, for example CA1707 off for test projects, before turning on `TreatWarningsAsErrors`. These figures are also in design.md.

## Skipped / uncertain
- **This repo's `docs/context/README.md` was not given the new quality-pass bullet.**
  - Why: `0002` step 7 forbids touching an existing context README, and the cmd limits the new exception to design.md.
  - Effect: it is now the one installed file that differs from the master, by that bullet. The root CLAUDE.md already names the filename, so nothing is lost.
  - If you want parity, add the bullet by hand.
- `docs/prompts/.gitkeep` is missing (`0002` step 4 lists it), but the folder is full of lane files, so the `.gitkeep` serves no purpose. I left it out.
- In `spec-drift.md` the new step is numbered `0b.`, beside step 0, as the cmd asked. In rendered Markdown it shows as a continuation of item 0 rather than a list item of its own. The text reads correctly; renumbering everything seemed out of scope.
- The cosmetic count (3) counts flagged concepts, not commits. The previous STATUS.md said 0 even though the evidence-layout and session-resume flags already existed then, so the earlier run probably counted differently.

## Self-answered questions
- **Where does the Quality baseline go when design.md doesn't exist yet?** In `0001` the baseline (step 6) comes before the spec sync that writes design.md (step 7). Answer: step 6 creates design.md with `# Technical` as its only h1 and just the baseline section, which is lint-valid, and step 7 fills in the rest. The same applies to projects without code, where step 7 is skipped.
- **Does `0002` also add the context README bullet to existing installs?** No. The cmd asked for one exception (design.md) only. The CLAUDE.md section, which is re-merged on every update, already carries the filename.
- **Exact placement of the STATUS line:** its own line under `Generated:`, separated by a blank line, before the first section.
- **What "advised" in this repo's design.md should say beyond the cmd's wording:** I included the `latest-recommended` warning counts as well as the plain-build count (0). The plain count alone would suggest that switching it on is free.
- **Whether the kit's conventions README (`docs/specs/README.md`) should describe the new STATUS line and the design.md halves:** yes. It is the conventions doc for both, and it is overwritten on update, so this repo's copy stays in sync.
