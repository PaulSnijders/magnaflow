# Code-quality watch (security, performance, clean code, sprawl, architecture)
Status: handed to architect -> decisions/0017-quality-pass.md, prompts/0026-cmd-kit-quality-pass.md (draft)
Supersedes: —

## Problem
Reframed (user, 2026-10-06): MagnaFlow itself is fine; nothing concrete went
wrong. The worry is Nxt Phase: several junior devs "just prompting". Plans and
answers look good, but code and specs are less structured; projects grow while
quality slowly lags. Nothing hurts yet; it will long-term. Wants to get ahead
of it with cheap quick wins that ask little of the developers - no control for
control's sake. Wants to adopt /health, /cso, /review, /security-review
structurally, needs guidelines.

## Research (2026-10-06, summary)
- Drift is real: GitClear (8x duplicated blocks 2024, refactoring share down),
  Veracode 2025 (45% of LLM output has OWASP flaws, C# 38-45%), DORA 2025
  (AI = amplifier; more instability), CodeScene 2026 (+30% defect risk in unhealthy code).
- Boeckeler, harness engineering (martinfowler.com, 2026-04): guides (specs,
  CLAUDE.md) vs sensors (tests, linters, review); deterministic sensors first,
  LLM review second. MagnaFlow has strong guides, weak sensors.
- OpenAI harness engineering: golden principles in repo + recurring cleanup
  agents opening small fix PRs + a quality doc grading domains/layers.
- .NET architecture tests (NetArchTest / ArchUnitNET) as plain xUnit: no drift.
- gstack /health: deterministic counts -> rubric -> history jsonl -> regressions.
- Failure modes: LLM 0-10 scores are noisy (binary checks vs named rules are
  far more stable); false positives kill attention; scores only help when a
  regression becomes a lane item.

## Premises
1. Target is company projects with juniors. - agreed
2. Juniors won't run audits themselves. - disputed: juniors should be
   educated and can pick this up. Adjusted: don't *depend* on it, but let
   the project owner (junior) run the periodic pass, senior reviews.
3. Deterministic sensors first, LLM yes/no second, no scores. - agreed
4. Company projects have PRs + CI (security-review action already runs). - agreed
5. Findings count only as backlog items. - agreed
6. Sprawl via cheap counts over time. - agreed
7. (user) Worry about double-checking and over-complication: building it
   properly is what matters most, and MagnaFlow already does that. - agreed in
   part: guides (specs, design.md, cmd flow) carry most weight; sensors only
   where deterministic and free.

## Finding in the kit
`0001-adopt-spec-system.md` step 6 has design.md written as "layout rules,
components, tone" - UI only. MagnaFlow's own design.md has engineering
principles + technology + "new dependencies need a concrete need". Adopted
repos may lack that half. /architect already reads design.md and refuses cmds
that bend its rules - so principles there are guides for every cmd, no new
machinery.

## Quick wins (user verdict)
1. Strict build (analyzers, warnings as errors, vuln scan in CI) - always good.
2. Golden principles - should come via mf-spec; make it explicit in 0001.
3. PR security-review action - already running.
4. Monthly /health + /cso pass - try it.
5. Architecture tests - later.

## Directions
A. Kit-only + habit (recommended): 0001 adopt (and 0002 update) asks for
   engineering principles in design.md (deps, layering, security basics,
   reuse-before-new) and checks a strict build is in place; a monthly pass
   (/cso, /health) by the project owner, findings -> cmds, senior reviews.
   Effort: small (prompt text). Risk: low. Reuses design.md, /architect, lane.
B. A + a `/quality` kit command mirroring /spec-drift: yes/no check of code
   vs design.md principles, dated report. Effort: medium. Risk: overlap with
   /cso and /review; another report to ignore. Only if A shows gaps.
C. Do nothing in MagnaFlow; a company guideline page only. Effort: tiny.
   Risk: not reaching the repos where it matters.

## Chosen
A (user, 2026-10-06): kit text + habit. Principles in design.md via 0001/0002;
0001 checks (not sets up) a strict build; monthly pass by the project owner.

## Monthly pass (agreed 2026-10-06; steps 1-5 below, tools revised)
Facts checked:
- gstack /health is JS/TS-oriented (tsc, eslint, biome, knip); on a .NET repo
  most categories SKIP. History goes to ~/.gstack/projects/<slug>/ -
  machine-local, not in git, invisible to the team. -> use on Angular
  frontends only; .NET signal = strict build + vuln scan in CI.
- gstack skills are interactive (AskUserQuestion); mf-worker runs claude -p
  headless -> the pass is not a lane cmd; the owner runs it in Claude Code.
Flow:
1. Owner runs /cso (+ /health on frontend) monthly.
2. Triage by the owner, senior reviews: real / not real per finding
   (the education moment). Not automatic: false-positive rates are high;
   auto-cmds would flood the lane.
3. Result recorded as docs/context/YYYY-MM-DD-quality-pass.md (outside audit
   = context genre): findings + verdict per finding, short.
4. Real findings -> /architect -> draft cmd(s) citing that context file.
5. /spec-drift reports the date of the newest *-quality-pass.md in STATUS.md
   and flags it when older than ~30 days (same trick as the 14-day expiry).

## Tools: built-in Claude Code skills over gstack (agreed)
- gstack: third-party install per dev, heavy preamble (telemetry, gbrain),
  interactive, JS-oriented /health, state in ~/.gstack. Not a company default.
- Built-ins everyone has: /security-review, /code-review (levels, path/branch
  target), /simplify. They are diff-scoped -> monthly pass reviews the
  changes since the last quality-pass (its date/commit = the base). Bounded,
  incremental, follows growth. Whole-repo deps/secrets: deterministic CI
  (vuln scan) instead of /cso.
- Findings -> cmd in the same conversation via /architect; never automatic.

## Revision (user, 2026-10-06): PR flow, not main
Company projects work on a branch; a PR every 1-2 weeks. Consequences:
- The pass moves from the calendar to the PR: the PR diff is the range.
  /code-review <PR> (high) + triage with the senior; findings -> /architect
  -> cmds; short docs/context/YYYY-MM-DD-quality-pass.md.
- /security-review stays in the pass (user): run it yourself before the PR,
  fix what is real, and the GitHub action then simply passes. Overlap is fine.
- /spec-drift >30-day flag stays as a safety net for stalled PR rhythm.
- /simplify (built-in: review changed code for reuse/simplification/efficiency
  and apply the fixes; no bug hunting): not in the pass (it edits, the pass
  only judges; /code-review already covers reuse/simplification). Optional
  developer habit on their own diff before opening the PR.
- 0001 strict build: ask the user - set it up, or advise only. User decides.

## Open questions
- Resolved by the PR flow: range = the PR; strict build = ask the user.
- Does the kit assume a PR flow now? MagnaFlow itself works on main; the
  pass must still work there (fallback: /code-review on the range since the
  last quality-pass).
- Which engineering principles are the kit default for design.md (generic
  list vs. asked from the user during adopt)?
- Where the quality-pass date comes from for /spec-drift: newest
  docs/context/*-quality-pass.md filename (lean) vs a STATUS.md line.

## For the architect
Read: this note; tools/mf-spec/spec-kit/0001-adopt-spec-system.md (step 6,
design.md = "layout rules, components, tone"); 0002-update-spec-system.md;
the /spec-drift command + spec_lint; docs/context/README.md; KIT.md rejected
list (CI gates were rejected for solo - the team case differs; note it).
Decide first: PR-based pass vs. main fallback, and how the kit phrases both. Scope is kit text
only (0001, 0002, spec-drift, CLAUDE-section, context README); no tool code.
Remember: kit change -> run 0002 on this repo too.
