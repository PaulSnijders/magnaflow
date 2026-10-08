---
date: 2026-07-02
topic: specs, spec-kit, strategy
status: accepted
---

# 0002 — Spec strategy — comparison, decision, and the road to live specs

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

Two spec systems were in use, answering different questions:

- **GitHub Spec Kit** specifies the **change**: one folder per feature,
  a one-way pipeline specify → clarify → plan → tasks → implement.
  Strong from vague idea to task plan, with quality gates. Weak after
  the merge: its specs are discarded scaffolding, nobody updates spec
  001 when feature 007 changes that behavior, and nothing describes
  what the program is *now* (only the constitution is permanent).
- **Our own system** (NxtPhase, from fastapi-nextjs-template) specifies
  the **state**: one spec per page plus `concepts/`, living alongside
  the code, with `/spec-drift` guarding both directions and the
  Help/`# Technical` split doubling as in-app help. Weak at driving new
  development: no path from idea to plan.

They complement each other: Spec Kit is the vision's *Planned Changes*
layer, ours the *Product/Interaction Specs* layer. Neither closes the
gap between them — folding a finished delta back into the state specs.
That fold-in, guarded afterwards by the drift check, is MagnaFlow's
actual reason to exist.

## Options

1. Use Spec Kit unmodified for all feature work, our system for state
   (the first decision, 2026-07-02).
2. Retire Spec Kit from the default workflow; work spec-first on the
   state specs, with delta weight proportional to risk.

## Measurement

Two real uses (see `0004-spec-kit-evaluation.md`):

- Worker Controller (greenfield): ≈ 1500 lines of spec artifacts for
  ≈ 1200 lines of code — heavy but tolerable; clarify/analyze caught
  real design flaws.
- ProjectA feature (brownfield, small delta): spec/code ratio 2.2:1,
  3.8:1 excluding a barely-touched file.

Spec Kit charges a **fixed overhead per feature** regardless of delta
size. Its artifacts justify a change for building at arm's length
(teams, autonomous agents) and are discarded after merge. Our work is a
stream of small features by a solo developer who is himself the review
loop; the fixed cost never pays back, and two spec systems double the
bookkeeping.

## Choice

Option 2 (2026-07-07), superseding option 1.

- **Kept from Spec Kit**: the constitution (ported into the repo's own
  docs), "ask concrete questions before building" as a habit rather
  than a phase, the pipeline shape for the rare heavy case, and its
  codebase as study material. `specify init` stays available for a
  large, hard-to-reverse feature.
- **Three tiers by risk, not code size**: small = edit the state spec
  and implement; medium = one lean delta doc; exceptional (migrations,
  auth, payments) = a full pipeline.
- **Way of working (2026-07-04)**: everything in one application
  (Claude Code). The old guarantee that a design session sees only
  `docs/` is rebuilt as a mechanism instead of an application boundary:
  behavior change updates its spec in the same commit, `hotfix:` as the
  tracked escape hatch, drift check as an audit that should always be
  green. The planned pre-commit gate was later dropped; see
  `tools/mf-spec/spec-kit/KIT.md` ("Deliberately not in the kit").
- **Writing principle**: specify decisions, not defaults.
- **End state** (aim, not built): nobody touches code, only specs. A
  spec that is ahead of the code is a work order; the system derives
  the delta from it, workers implement, drift goes green. Open: who may
  generate that delta, and how small it should be.

Current behavior: tiers and lane in `tools/mf-spec/README.md` and
specs/README.md; writing rules in specs/README.md and
`tools/mf-spec/system.md`; the constitution's successor is
specs/concepts/design.md. Granularity beyond pages was answered by the
surfaces model in `0003-own-spec-system-v2.md`.
