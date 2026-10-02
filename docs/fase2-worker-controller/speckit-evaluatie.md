# Evaluation of GitHub Spec Kit — after feature 001 (Worker Controller)

> Step 7 from the step-by-step plan: what worked well, what did not, and
> what MagnaFlow's own spec layer must do differently. This was the actual
> research question of phase 2. Recorded 2026-07-04; extend after the
> first real dogfood runs.

## The flow as run (2026-07-02)

constitution → specify → clarify (4 questions) → plan → tasks → analyze
(6 findings, 5 remediated) → implement (29 tasks, 60 unit tests, 5
quickstart scenarios validated with a stub agent).

## What worked well

- The pipeline ran smoothly end to end, including the implementation pass.
- **Clarify paid for itself**: agent autonomy (configurable per project),
  branch base (`base:` field) and dirty-tree refusal all came out of its
  targeted questions — all three went straight into the implementation.
- **Analyze caught a real design flaw** before implementation: task.md
  writes while the work branch was checked out (I1) would have caused
  checkout conflicts in production.
- **The constitution worked as a touchstone**: the principles (one-shot,
  plain text, git as database) gave a direct answer at every design
  choice; the Constitution Check in plan.md was not a ritual.
- After development, additional remarks could go directly into Claude
  Code; the spec artifacts (FRs, contracts, clarifications) grew neatly
  along with the changes.
- End result: complete tool with tests, example project and synchronized
  specs.

Conclusion: we continue with Spec Kit for delta specs (building features).

## What did not work well / friction

- **Volume**: spec + plan + contracts + tasks ≈ 1500 lines of
  documentation for a single tool of ~1200 lines of code. For a tool of
  this size the ratio feels heavy.
- **Redundancy between artifacts**: FRs, data model, contracts and tasks
  partly repeat each other; changes (such as the analyze remediations)
  must be applied in 3–4 places.
- **Brownfield confirms it (ProjectA, 2026-07-07)**: for a small
  feature on an existing app the spec/code ratio was 2.2:1 — and 3.8:1
  excluding a barely-touched file. The overhead is fixed per feature,
  so the smaller the delta, the worse the ratio.

## Critical reflection: why a separate Worker Controller?

If Claude Code + Spec Kit already does this so smoothly, what does the
controller add? Answer after discussion:

- **Interactive work wins** as long as one human drives one feature at a
  time.
- **The controller wins** at: volume without a human present
  (batch/overnight work), reproducibility (every run deterministically
  built from spec + task, with logs and result.yml as evidence), and as a
  building block for the dispatcher and distributed workers.

**Decision: the Worker Controller is optional in the workflow.** The task
format in `.magnaflow/tasks/` is the interface; who executes the task — a
human with Claude Code or the controller — is interchangeable. Further
development of the controller is not a priority until volume or
automation calls for it.

## Lessons for MagnaFlow's own spec layer

- The task status model (pending/running/done/failed in frontmatter)
  worked exactly as hoped: git diff shows the life cycle. Extend this
  pattern to the spec layer (proposed/accepted/…).
- Consider one coherent spec artifact per feature instead of five
  separate files — or make the coupling machine-checkable (analyze did
  this manually).
- The human-in-the-loop moments (clarify answers, analyze remediation
  choices) were the most valuable interactions; do not automate those
  away — but make them a *habit* (ask concrete questions before
  building), not a document-producing phase.

## Final verdict (2026-07-07)

**Spec Kit is retired from the default workflow.** It is built for
building at arm's length — teams, autonomous agents, high-stakes
changes — where a fixed per-feature overhead buys de-risking. Our
context (solo, fast, small features, human continuously in the loop)
never recoups that fixed cost, and its delta artifacts are scaffolding
with no life after merge. Full decision and the new three-tier default
flow: `docs/kennis/spec-strategie.md`. What we keep: the constitution
concept, the questions-first gate as a habit, the pipeline shape for
exceptional cases, and its codebase as study material for mf-spec.

## Focus going forward: integrating our own spec system

The main line becomes the combination from `docs/kennis/spec-strategie.md`:

- **Spec Kit** produces the delta specs: what must be built/changed.
- **Our own spec system** (pages/concepts, Help/`# Technical`, spec-drift)
  is the continuously updated state: what the product *is*.
- The **fold-in step** still to be designed connects them: after implement
  the state specs are updated from the delta; the drift check then stands
  guard.

Possible first experiment: adopt our own spec system in the example
project (hello-website), run one feature there via Spec Kit and do the
fold-in step once by hand (`/spec` for the affected pages +
`/spec-drift`). Then we know exactly what the fold-in step must be able
to do before we automate it. Hook point for later:
`.specify/extensions.yml` has an `after_implement` hook.
