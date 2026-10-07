# mf-spec — system description (current state)

How the system works, as it is now. Usage: [README.md](README.md).
Reasons behind the conventions:
[conventions-rationale.md](conventions-rationale.md). History:
`docs/decisions/`.

## Why

A large project does not fit in an AI context window, and every session
starts blank. State specs are a compact projection of the code —
measured ~20× smaller — so the AI works at spec level and reads code
only when needed. The same files are the in-app help for users and the
documentation for the team. Specs describe **state** (what the product
is), not changes; change documents are disposable scaffolding.

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
docs/context/       # what the outside world said — YYYY-MM-DD-slug.md
docs/prompts/       # delta lane: NNNN-cmd/rst/qa-name.md
scripts/spec_lint.mjs  # format lint (Node, no deps), run by /spec-drift
.magnaflow/         # runtime plane: machine-local; config.yml committed
```

- **Context** (`docs/context/`): one file per conversation, managed —
  corrected and cleaned up, never changed in substance.
- **Delta lane** (`docs/prompts/`): inside the docs plane, so design
  sessions can write commands and read results without seeing code.
  - `NNNN-cmd-name.md` — the command; status in its frontmatter.
  - `NNNN-rst-name.md` — the report, written for the next AI session.
    Its frontmatter carries a one-line `summary:`, the human-sized
    layer that the cockpit renders in its lane.
  - `NNNN-qa-name.md` — question/answer dialogue, only when needed.
  - The lane itself is the overview (status in each cmd's frontmatter,
    rendered by the cockpit); there is no central status file.
- **Delta-lane numbering**: `NNNN` is the highest number on disk in
  `docs/prompts/` plus one, never a number the conversation remembers.
  Follow-ups keep the parent's number and add a letter.
- **Runtime plane** (`.magnaflow/` at the repo root, created by worker,
  watcher and mf-run): machine-local in full — the `<id>/` run logs,
  `<id>/session.yml`, `mf-watch.log`, `mf-watch.lock` and `run/`
  (mf-run's PID files and service logs). Only `config.yml`, the
  project's worker config, is committed. Gitignored
  as `.magnaflow/*` + `!.magnaflow/config.yml`. Ignored rather than
  left untracked: untracked files dirty the very tree the watcher
  polls, and the worker's dirty-tree guard then blocks every run.
- **Surfaces**: where users/systems touch the product (app frontend,
  static site, admin, API). Each declares a code root, a routes glob
  (the whole "adapter"), and help behavior (`help`, `help_language`,
  `languages`). One uniform rule: every surface gets its folder.
- **Pages**: one spec per route; the path mirrors the route.
- **Concepts**: logic that shows up on several pages but belongs to
  none. Tracked for drift via a trailing `Code:` line. The threshold is
  positive: a mechanism that determines behavior and cannot be read off
  one module gets a concept, even if only one page shows it.
  `concepts/design.md` is the design system reference, in two halves:
  engineering principles (layering, dependency policy, security basics,
  reuse before new code — `/architect` checks every cmd against them)
  and, when there is a UI, appearance. Page specs never describe
  appearance; they reference it and record deviations. Its Quality
  baseline section records which mechanical checks (warnings as errors,
  analyzers, linter, vulnerability scan) are on and which were only
  advised — asked once at adoption.

## File format

Two valid forms, for pages and concepts alike:

- **With user help**: exactly two h1s — title, help text, `# Technical`.
  Help is canonical in the surface's `help_language`; translations are
  generated derivatives (content-hash tracked), never hand-edited.
  `# Technical` is always English.
- **Without user help** (admin pages, technical concepts): the file
  starts with `# Technical` as its only h1.

A title with an empty help section is a format error. Rendering is
fail-closed: users only ever see the full two-h1 form.

## Writing rules (essence)

Specify decisions, not defaults; unspecified means it may vary, so pin
what must stay stable (URLs, data formats, user-facing terms). Short
beats complete. Symbol names, not line numbers; anchor slugs, not
section numbers. `BUG:` and `DRAFT:` prefixes keep known bugs and
unreviewed generated specs greppable.

## Keeping specs true

- **Spec-first rule**: behavior change ⇒ owning spec updated in the
  same turn/commit. The `hotfix:` prefix is the tracked escape hatch.
- **Deepen on touch**: if the owning spec is too thin to review your
  change against, raise it first; never deepen untouched areas.
- **Drift check as audit** (`/spec-drift`): git-date comparison per
  surface glob and concept `Code:` lines; behavioral vs cosmetic is
  judged only for flagged items. Should always be green; a finding
  means the enforcement has a gap. Only behavioral drift is listed.
  Cosmetic diffs are counted, because a report that repeats what you
  already dismissed teaches you to skip the report.
- **A clean STATUS.md cannot mean "never checked"**: a `Generated:`
  date older than 14 days, or missing, is itself a finding on the next
  `/spec-drift`. Findings are fixed at their enforcement gap, never
  accepted into a side list (kit 1.0 dropped the sha-pinned
  `ACCEPTED.md`).
- **Quality pass** (kit 1.2): per pull request, or about monthly on a
  repo without PRs, `/security-review` and `/code-review` (built-in
  Claude Code skills) on the change; a human triages each finding, the
  record is `docs/context/YYYY-MM-DD-quality-pass.md`, real findings
  become cmds via `/architect` — never automatically. `/spec-drift`
  writes `Quality pass: <date> (<N> days ago)` (or `none`) under
  `Generated:`; older than 30 days is a finding, `none` is not. Why:
  `docs/decisions/0017-quality-pass.md`.
- **Format lint** (`scripts/spec_lint.mjs`, run by `/spec-drift`):
  split-marker shape, surface folders vs `config.yml`, anchor
  uniqueness and resolution, concept `Code:` paths. Mechanical. A
  format problem outranks a stale spec, because a broken marker leaks
  admin content.
- **Brownfield**: spec-first is a property of changes, not of the
  codebase. The initial sync generates `DRAFT:` specs from code
  (existing hand-written help as input); coverage deepens where the
  code is worked on.

## Session continuity (`resume:`)

Follow-up suffixes (`NNNNB-`, `NNNNC-`, …) only group and sort for
humans; session continuity comes from `resume:` in the cmd
frontmatter. Two forms, told apart by shape:

- **A command id** (`0008-funnel`, `0008B-funnel`) — resolved at run
  time to that command's recorded session
  (`.magnaflow/<id>/session.yml`), so a lineage keeps working even
  when the session id is re-recorded.
- **A raw session id** — anything not id-shaped, passed to
  `claude --resume` verbatim. mf-cockpit's "continue on this command"
  writes this form.

Id resolution also accepts the cmd filename, since that is what a
human usually has at hand:

1. A verbatim `.magnaflow/<value>/` match wins (so a slug that really
   starts with a lane word still resolves to itself).
2. Otherwise the directory and `.md` are dropped, and a lane segment
   directly after the number is stripped:
   `docs/prompts/0008-cmd-funnel.md` reaches `0008-funnel`, while
   `0012-fix-qa-export` stays itself.

Session ids are machine-local: the agent resolves one against a
transcript stored per machine and per checkout path. So `session.yml`
stays out of git; a lineage is reproducible on the machine that ran it
and starts fresh elsewhere.

An unresolvable `resume:` is a hard error, never a silent fresh
session: quietly losing the parent's context is worse than stopping.
The message names every id tried and the commands that have actually
run, so the fix is a one-line edit.

## Branches

Default is trunk: no branch, spec and code in the same commit. A branch
is a per-change opt-in (`branch:`/`base:` in cmd or task frontmatter —
same fields, same meaning in both lanes) for changes that span
sessions, run in parallel, or want the PR gate.

The invariant is the two-plane rule proven in the Worker Controller:

- **Bookkeeping** (cmd/rst, task status, reports) commits on the
  invoking branch — the branch the executor was started from, normally
  trunk.
- **Code and its spec updates** travel in the work branch (created
  from `base:`, default main/master) and arrive by merge.

So the trunk always shows the live queue, and a worker machine sits on
trunk and branches per task. A branchless run lands as a single
commit — status transition and change together, the claim commit
amended once the run ends. A branched run keeps the split, because
there the two planes really are two histories. Executors push the
invoking branch and the work branch when a remote exists; they do not
create a PR — opening and merging it stay human.

## Enforcement (current vs planned)

**Current**: the skill and the CLAUDE section carry the rules into
every session. `scripts/spec_lint.mjs` (kit 1.0) checks the hard,
objective class — split-marker shape, layout vs config, anchors,
`Code:` paths — and `/spec-drift` runs it locally. There is
deliberately no CI gate (see "Deliberately not in the kit" in
[KIT.md](spec-kit/KIT.md)).

**Planned**: the worker controller calls the same lint, so it fires on
every run in every target repo and hard findings fail the run. **Soft**
signals (a route without a spec, code touched without its spec, `docs/`
root growth, an expired STATUS.md date) never fail anything. They show
as a per-project level in the cockpit, which can count
`git rev-list --count --since=<Generated date> -- <code roots>` itself.
A blocker that is sometimes wrong teaches people to bypass the gate, so
the pressure is proportional instead of a wall. Design:
`docs/decisions/0016-genres-and-gate.md`. Also planned: hooks
(session-start STATUS summary) and generated help translations.

## Distribution

Master kit: `tools/mf-spec/spec-kit/`; its contents and install/update
steps are in [KIT.md](spec-kit/KIT.md). The version stamp in the
conventions README travels with every update; `0002` is idempotent and
brings any older install to the current version (1.2) in one run. The
kit is tool-neutral: MagnaFlow's worker config and ignore lines are one
optional question in 0001/0002. Phase 0: copy folder + prompt. Phase 1:
`mf-spec init`/`update` automates the same. Installation order relative
to other tooling must never matter.

## Where it sits in MagnaFlow

Thinking happens in a Claude Code session under the `/brainstorm` and
`/architect` skills, which write to `docs/` only. Execution happens in
Claude Code (`/cmd-go` picks up the next `ready` cmd) or via the
Worker Controller (`tools/worker-controller/`,
see `docs/decisions/0005-worker-v0-2-direction.md`). The state specs
are the shared truth both planes work against.
