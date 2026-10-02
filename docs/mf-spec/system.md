# mf-spec — system description (current state)

What the system is and why each piece exists. No history — see
`docs/fase*/` and `docs/kennis/` for how we got here.

## Why

A large project does not fit in an AI context window, and every session
starts blank. State specs are a compact projection of the code —
measured ~20× smaller — so the AI works at spec level and dives into
code only when needed. The same files are the in-app help for users and
the documentation for the team. Specs describe **state** (what the
product is), not changes; change documents are disposable scaffolding.

## Building blocks (inside a target repo)

```text
docs/specs/
  README.md         # conventions (shipped from the kit)
  config.yml        # surface declarations
  STATUS.md         # generated exception report — never hand-edited
  _overview.md      # the application as a whole
  concepts/         # cross-cutting logic; design.md always exists
  <surface>/        # one folder per surface, mirroring its routes
docs/decisions/     # why it became this way — NNNN-slug.md, frozen
docs/context/       # what the outside world said — YYYY-MM-DD-slug.md,
                    # one file per conversation, managed: corrected and
                    # cleaned up, never changed in substance
scripts/spec_lint.mjs  # format lint (Node, no deps), run by /spec-drift
docs/prompts/       # delta lane: NNNN-cmd-name.md (command, status in
                    # frontmatter) + NNNN-rst-name.md (report; its
                    # frontmatter carries a one-line summary: — the
                    # human-sized layer over a report written for the
                    # next AI session, rendered in the cockpit's lane) +
                    # NNNN-qa-name.md (question/answer dialogue, only
                    # when needed). Follow-ups: NNNNB-, NNNNC-, …
                    # (suffix is human-facing grouping/sorting only;
                    # session continuity comes from resume: in the
                    # frontmatter) — inside the docs plane so design
                    # sessions can write commands and read results
                    # without seeing code; overviews are generated
                    # (STATUS.md), never a central status file
.magnaflow/         # runtime plane (repo root, created by worker,
                    # watcher & mf-run): machine-local in full and
                    # gitignored as `.magnaflow/*` +
                    # `!.magnaflow/config.yml` — the <id>/ run logs,
                    # <id>/session.yml, mf-watch.log, mf-watch.lock and
                    # run/ (mf-run's PID files + service logs) never
                    # travel; only config.yml, the project's worker
                    # config, is committed. Ignored rather than left
                    # untracked: untracked they dirty the very tree the
                    # watcher polls, and the worker's dirty-tree guard
                    # then blocks every run
```

- **Delta-lane numbering**: `NNNN` is read off disk — the highest number
  present in `docs/prompts/` plus one — never from the numbers a
  conversation happens to remember. A thread that is parked and resumed
  later has a stale view: the number it thinks is next has usually been
  taken by work that ran in between. Follow-ups do not take a new
  number at all; they keep the parent's and add a letter.
- **Surfaces**: where users/systems touch the product (app frontend,
  static site, admin, API). Each declares a code root, a routes glob
  (the whole "adapter"), and help behavior (`help`, `help_language`,
  `languages`). One uniform rule: every surface gets its folder.
- **Pages**: one spec per route; path mirrors the route.
- **Concepts**: logic that surfaces on several pages but belongs to
  none. Tracked for drift via a trailing `Code:` line. Threshold is
  positive: a mechanism that determines behavior and cannot be read off
  one module gets a concept, even if only one page shows it.
  `concepts/design.md` is the design system reference: page specs never
  describe appearance, they reference it and record deviations.

## File format

Two valid forms, for pages and concepts alike:

- **With user help**: exactly two h1s — title, help text, `# Technical`.
  Help is canonical in the surface's `help_language`; translations are
  generated derivatives (content-hash tracked), never hand-edited.
  `# Technical` is always English.
- **Without user help** (admin pages, technical concepts): the file
  starts with `# Technical` as its only h1.

A title with an empty help section is a format error — "forgotten" and
"deliberately none" must not look alike. Rendering is fail-closed:
users only ever see the full two-h1 form.

## Writing rules (essence)

Specify decisions, not defaults; unspecified = may vary, so pin what
must stay stable (URLs, data formats, user-facing terms). Short beats
complete. Symbol names, not line numbers; anchor slugs, not section
numbers. `BUG:` and `DRAFT:` prefixes keep known bugs and unreviewed
generated specs greppable.

## Keeping specs true

- **Spec-first rule**: behavior change ⇒ owning spec updated in the
  same turn/commit. `hotfix:` prefix is the tracked escape hatch.
- **Deepen on touch**: if the owning spec is too thin to review your
  change against, raise it first; never deepen untouched areas.
- **Drift check as audit** (`/spec-drift`): git-date comparison per
  surface glob + concept `Code:` lines, behavioral-vs-cosmetic judged
  only for flagged items. Should always be green; a finding means the
  enforcement has a gap. Only behavioral drift is listed — cosmetic
  diffs are counted, because a report that repeats what you already
  dismissed teaches you to skip the report, which is the stale-clean
  failure by another route.
- **A clean STATUS.md cannot mean "never checked"**: its `Generated:`
  date older than 14 days, or missing, is itself a finding on the next
  `/spec-drift`. Findings are fixed at their enforcement gap, never
  accepted into a side list (kit 1.0 dropped the sha-pinned
  `ACCEPTED.md`; see `ontwerp-conventions-rationale.md`).
- **Format lint** (`scripts/spec_lint.mjs`, run by `/spec-drift`):
  split-marker shape, surface folders vs `config.yml`, anchor
  uniqueness and resolution, concept `Code:` paths. Mechanical; a
  format problem outranks a stale spec because a broken marker leaks
  admin content.
- **Brownfield**: spec-first is a property of changes, not of the
  codebase. Initial sync generates `DRAFT:` specs from code (existing
  hand-written help as input); coverage deepens where the code lives.

## Session continuity (`resume:`)

The `NNNNB-` letter suffix is human-facing grouping and sorting only;
continuity comes from `resume:` in the cmd frontmatter. Two accepted
forms, distinguished by shape:

- **A command id** (`0008-funnel`, `0008B-funnel`) — resolved at run
  time to that command's own recorded session
  (`.magnaflow/<id>/session.yml`), so a lineage keeps working even
  when the session id is re-recorded.
- **A raw session id** — anything not id-shaped, handed to
  `claude --resume` verbatim. This is what mf-cockpit's "continue on
  this command" writes.

Id resolution accepts the cmd filename too, since that is what a human
usually has at hand: a verbatim `.magnaflow/<value>/` match wins first
(so a slug that genuinely starts with a lane word still resolves to
itself), otherwise directory and `.md` are dropped and a lane segment
in the infix position — directly after the number — is stripped, so
`docs/prompts/0008-cmd-funnel.md` reaches `0008-funnel` while
`0012-fix-qa-export` stays itself.

Session ids are machine-local: the agent resolves one against a
transcript stored per machine and per checkout path. `session.yml`
therefore stays out of git (see the runtime plane above) — a lineage is
reproducible on the machine that ran it, and starts fresh elsewhere.

An unresolvable `resume:` is a hard error, never a silent fresh
session: quietly losing the parent's context is worse than stopping.
The message names every id tried and the commands that have actually
run, so the fix is a one-line edit instead of an investigation.

## Branches

Default is trunk: no branch, spec + code in the same commit. A branch
is a per-change opt-in (`branch:`/`base:` in cmd or task frontmatter —
same fields, same semantics in both lanes) for changes that span
sessions, run in parallel, or want the PR gate. The invariant is the
two-plane rule proven in the Worker Controller: bookkeeping (cmd/rst,
task status, reports) commits on the base branch; code and its spec
updates travel in the work branch and arrive by merge. The trunk
therefore always shows the live queue, and a worker machine simply
sits on trunk and branches per task. A branchless run lands as a
single commit — status transition and change together, the claim
commit amended once the run ends — while a branched run keeps the
split, because there the two planes really are two histories.
Executors (Claude Code / worker) create the PR; merging stays human.

## Enforcement (current vs planned)

Current: skill + CLAUDE section carry the rules into every session;
`scripts/spec_lint.mjs` (kit 1.0) checks the hard, objective class —
split-marker shape, layout vs config, anchors, `Code:` paths — and
`/spec-drift` runs it locally. There is deliberately no CI gate (see
"Deliberately not in the kit" in `KIT.md`).

Planned: the worker controller calling the same lint so it fires on
every run in every target repo, hard findings failing the run. **Soft**
signals (a route without a spec, code touched without its spec, `docs/`
root growth, an expired STATUS.md date) never fail anything and surface
as a per-project level in the cockpit, which can count
`git rev-list --count --since=<Generated date> -- <code roots>` itself.
A blocker that is sometimes wrong is what teaches people to bypass the
gate, so the pressure is proportional instead of a wall. Design:
`docs/mf-spec/ontwerp-genres-and-gate.md`. Also planned: hooks
(session-start STATUS summary) and generated help translations.

## Distribution

Master kit: `tools/mf-spec/spec-kit/` (KIT.md, conventions README with
a version stamp, config template, adopt prompt 0001, update prompt 0002,
`/spec`, `/spec-drift`, `scripts/spec_lint.mjs`, specs skill, CLAUDE
section, record-folder READMEs). Versioning: the stamp in the
conventions README travels with every update; 0002 is idempotent and
brings any older install to the current version (1.1) in one run. The kit is tool-neutral —
MagnaFlow's worker config and ignore lines are one optional question in
0001/0002. Phase 0: copy folder + prompt. Phase 1: `mf-spec
init`/`update` automates the same; installation order relative to other
tooling must never matter.

## Where it sits in MagnaFlow

Thinking happens in Cowork against `docs/` only; execution in Claude
Code or via the Worker Controller (`tools/worker-controller/`, see
`docs/fase2-worker-controller/v0.2-direction.md`). The state specs are
the shared truth both planes work against.
