---
date: 2026-07-04
topic: spec-kit, evaluation
status: accepted
---

# 0004 — Evaluation of GitHub Spec Kit — after feature 001 (Worker Controller)

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

Phase 2's research question: what does GitHub Spec Kit do well, what
not, and what must MagnaFlow's own spec layer do differently? Feature
001 (the Worker Controller) ran the full flow on 2026-07-02:
constitution → specify → clarify (4 questions) → plan → tasks → analyze
(6 findings, 5 remediated) → implement (29 tasks, 60 unit tests, 5
quickstart scenarios validated with a stub agent).

A second question surfaced: if Claude Code plus Spec Kit already builds
this smoothly, what does a separate Worker Controller add?

## Options

- Spec Kit: keep it for delta specs, or retire it from the default
  workflow.
- Worker Controller: make it the required executor, or one optional
  executor among others.

## Measurement

**Worked well**
- The pipeline ran end to end, implementation included.
- Clarify paid for itself: per-project agent autonomy, the `base:`
  field and dirty-tree refusal all came from its questions.
- Analyze caught a real flaw before implementation: task-file writes
  while the work branch was checked out would have caused checkout
  conflicts.
- The constitution was a real touchstone (one-shot, plain text, git as
  database), not a ritual.
- Follow-up remarks after development grew the artifacts neatly.

**Friction**
- Volume: ≈ 1500 lines of documentation for ≈ 1200 lines of code.
- Redundancy: FRs, data model, contracts and tasks repeat each other;
  one change lands in 3–4 places.
- Brownfield (ProjectA, 2026-07-07): spec/code ratio 2.2:1, 3.8:1
  excluding a barely-touched file. The overhead is fixed per feature,
  so the smaller the delta, the worse the ratio.

**Worker Controller**: interactive work wins while one human drives one
feature at a time. The controller wins at volume without a human
present (batch, overnight), reproducibility (every run built from spec
plus task, with logs as evidence), and as the building block for a
dispatcher and distributed workers.

## Choice

- **Spec Kit**: the first verdict (2026-07-04) was to continue with it
  for delta specs. After the ProjectA data it was **retired from the
  default workflow** (2026-07-07): built for building at arm's length,
  where a fixed per-feature overhead buys de-risking; our solo,
  human-in-the-loop context never recoups it. Decision and the
  three-tier flow: `0002-spec-strategy.md`.
- **Worker Controller is optional.** The task format is the interface;
  who executes it — a human with Claude Code or the controller — is
  interchangeable. No further development until volume or automation
  calls for it (resumed in `0005-worker-v0-2-direction.md`).
- **Lessons for our own spec layer**: status in frontmatter worked
  exactly as hoped (git diff shows the life cycle) — extend it to the
  spec layer. Prefer one coherent artifact per change over five
  separate files, or make the coupling machine-checkable. The
  human-in-the-loop moments (clarify answers, remediation choices) were
  the most valuable; keep them, but as a habit of asking concrete
  questions before building, not a document-producing phase.

The proposed experiment (Spec Kit delta plus a manual fold-in on
hello-website) was overtaken by `0003-own-spec-system-v2.md`; fold-in
is now ordinary spec-first work (`tools/mf-spec/spec-kit/KIT.md`).
