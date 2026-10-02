# Spec strategy — comparison, decision, and the road to live specs

> Recorded 2026-07-02. Compares GitHub Spec Kit with our own spec
> system (NxtPhase, from fastapi-nextjs-template), pins down the decision
> on how we use both, and sketches how MagnaFlow turns this into "live
> specs".

## Two kinds of specs

The two systems answer a different question:

- **GitHub Spec Kit** specifies the **change** (delta): one folder per
  feature (`specs/NNN-name/`, tied to a git branch) with a
  one-way pipeline: specify → clarify → plan → tasks → implement.
  Strong at: going from vague idea to executable task plan, with quality gates.
  Weak at: life after the merge. Specs are discarded scaffolding; nobody updates
  spec 001 when feature 007 changes that behavior. There is no layer that
  describes what the program is *now* — the only permanent artifact is the
  constitution. Even `converge` works strictly within a single feature.

- **Our own system** specifies the **state**: one spec per page
  (path structure mirrors the routes) plus `concepts/` for
  cross-page logic. Specs live on forever alongside the code;
  `/spec-drift` guards both directions (code newer = stale, spec
  newer = implementation pending) via git dates. Strong at: the truth
  about the existing product, and the Help/`# Technical` split which makes
  specs double as in-app help — documentation that users see gets
  maintained. Weak at: driving new development; there is no path from idea
  to plan and tasks.

They clash nowhere and complement each other exactly. In MagnaFlow vision terms:
Spec Kit ≈ the *Planned Changes* layer, our system ≈ the
*Product/Interaction Specs* layer ("help pages are part of the
specifications").

## Decision: Spec Kit retired from the default workflow (2026-07-07)

Superseded earlier decision ("use Spec Kit unmodified to build MagnaFlow
tools"). After two real uses the data was consistent:

- Worker Controller (greenfield tool): spec artifacts ≈ 1500 lines for
  ≈ 1200 lines of code — heavy but tolerable; clarify/analyze caught
  real design flaws.
- ProjectA feature (brownfield, small delta): spec/code ratio
  2.2:1, or 3.8:1 excluding a barely-touched file.

The diagnosis: Spec Kit charges a **fixed overhead per feature**
(spec + plan + research + data-model + contracts + quickstart + tasks +
checklists) regardless of delta size. Its artifacts are
change-justification scaffolding for building at arm's length (teams,
autonomous agents) — discarded after merge. Our work is a stream of
small features by a solo dev who is himself the review loop; the fixed
cost never pays back, and running two spec systems doubles bookkeeping.

**What we keep from it:** the constitution (the one non-scaffolding
artifact — port it into the repo's own docs), the "ask concrete
questions before building" gate as a habit rather than a phase, the
pipeline shape (specify→plan→tasks) for the rare heavy case, and its
codebase as study/steal material for our own tooling. Spec Kit remains
available by exception: `specify init` is one command away for a
genuinely large, hard-to-reverse feature.

## Default flow (from 2026-07-07)

Three tiers, weight proportional to risk — not to code size:

1. **Small (default):** spec-first directly on the state specs. Change
   the state spec (spec ahead = work order) → implement → drift green.
   No delta artifact beyond the conversation and the commit.
2. **Medium:** one lean, disposable delta doc (goal, decisions, task
   list — a single file), preceded by the questions-first habit.
3. **Exceptional** (migrations, auth, payments, hard-to-reverse):
   full pipeline — re-init Spec Kit or equivalent.

## The road to live specs

The goal: specs that never go stale and also drive the product. The cycle
that connects both worlds:

```text
        (1) wish/idea
             │
             v
   delta: conversation or one lean
   delta doc (heavy pipeline only
   by exception)                       ← later driven by Spec Worker
             │
             v
   implementation by workers           ← Worker Controller
             │
             v
   (2) FOLD IN: delta is merged
       into the state specs            ← the step that exists nowhere today
             │
             v
   state specs = truth                 ← our system: pages/concepts,
       + in-app help                     Help/# Technical split
             │
             v
   (3) drift guarding                  ← /spec-drift model: git dates,
       code ↔ spec in both directions    lint, STATUS.md as worklist
             │
             └──> back to (1)
```

The fold-in step (2) is the gap neither system closes and thereby
MagnaFlow's actual reason to exist: after implementation a worker updates the
state specs based on the delta spec and the code, and the
drift check then guards that everything stays in sync.

## End state: fully spec-first

Eventually nobody (human nor design session) touches code anymore — only
specs:

1. You change a state spec (or talk to the Spec Worker, which does it
   for you). The spec is now "ahead" — that is not an error but a work order.
2. The system signals "spec ahead", generates a delta from it
   (tasks in `.magnaflow/tasks/`), and the workers implement.
3. Drift check runs; all green = spec, code and help are one again.

This makes the state spec the control panel of the product. The
Help section additionally guarantees that every change immediately yields visible
user documentation.

## Way of working (decided 2026-07-04)

Everything happens in one application (Claude Code) — writing specs and
implementing. The old separation (design session sees only `docs/`) was
valuable as a *hard guarantee*; we rebuild that boundary as a mechanism
instead of as an application boundary:

- **Commit rule:** a commit that touches code without a spec changing
  along with it is rejected (pre-commit check / lint extension).
- **Escape hatch:** a `hotfix:` prefix is allowed through, but automatically
  adds a debt line to STATUS.md — the fold-in then still happens, later.
- **Follow-up remarks** during/after implementation that change behavior
  land in the spec in the same turn (the same commit rule covers this).
- **Drift check becomes an audit:** it should always be green. If it does
  find something, there is a gap in the enforcement — fix the gap, not
  just the spec.

## Writing principle: specify decisions, not defaults

A spec must be complete in **intent**, not in enumeration. Test per
detail: *if the implementer chooses differently here than I had in mind,
do I care?* If not — leave it out.

- Conventions (save/cancel buttons, standard CRUD behavior) are
  not specified; only **deviations** from convention are
  information ("no cancel button, save immediately").
- Overspecifying is harmful: every line is maintenance burden and
  drift surface, and freezes accidental choices into requirements.
- Because of AI regeneration: **unspecified = may vary**. So do pin
  what must stay stable even if it is "obvious": URLs,
  data formats, terminology users know.
- Appearance is not described per page in prose but via reference
  to a design system/template ("standard form layout"). The
  design part of specs contains rules and references; page specs stay
  behavioral.
- Make deliberate openness explicit where the clarify phase would otherwise
  keep probing: "standard CRUD, nothing special" is a signal, not a
  specification.

## Still to design (open questions)

- Who may generate the delta from a spec change — and how small should
  such a delta be?

Answered elsewhere: granularity beyond pages is addressed by the
*surfaces* model in `docs/fase3-eigen-spec-systeem/ontwerp-v2.md`
(per-surface enumeration rules; `concepts/` with a `Code:` line as the
universal fallback).
