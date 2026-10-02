<!--
Sync Impact Report
- Version change: 1.0.0 → 1.1.0
- Modified principles: none renamed; existing principles I–VI confirmed by
  explicit user input (2026-07-02)
- Added sections:
  - Principle VII. Local-First, Open Formats (new)
  - Development Workflow: mandatory automated tests for core logic
    (was: tests only via task loop)
- Removed sections: none
- Templates requiring updates:
  - ✅ .specify/templates/tasks-template.md — "Tests" note updated: core-logic
    tests are mandatory per constitution, other tests remain optional
  - ✅ .specify/templates/plan-template.md — Constitution Check gate is generic;
    gates derive from this file at plan time, no edits needed
  - ✅ .specify/templates/spec-template.md — no constitution-specific sections
- Follow-up TODOs: none
-->

# MagnaFlow Constitution

MagnaFlow is a spec-first development platform, built as a collection of
small, deterministic tools. The repository and its knowledge are the source
of truth; AI is a replaceable executor. This constitution governs all tools
built in this monorepo (starting with the Worker Controller in
`tools/worker-controller/`).

## Core Principles

### I. Small, Deterministic, Composable Tools

Every MagnaFlow tool MUST be a small, single-purpose program that does its job
and stops (one-shot execution). Tools MUST NOT poll, schedule, or decide "what
next" — orchestration belongs to separate tools that compose them (e.g., a
future dispatcher calls the controller). Tools MUST behave deterministically:
the same inputs produce the same actions, with all variability coming from the
external processes they orchestrate.

Rationale: small deterministic tools are testable, debuggable, and composable;
distributed execution later becomes a matter of calling the same tools on
other machines.

### II. Plain Text Is the Interface

Everything that matters MUST be plain-text files: Markdown for
human-oriented content, YAML for machine-oriented data, YAML frontmatter
where both meet (e.g., `task.md`). No databases, no binary formats, no hidden
state. A task's identity lives in its folder name — never duplicated in file
contents — so identifiers cannot drift out of sync.

Rationale: plain text is diffable, greppable, editable by humans and AI alike,
and requires zero infrastructure.

### III. Git Is the Database

The repository is the single source of truth and the history. Every
meaningful state change (task status transitions, execution results, logs)
MUST be committed, so git history shows the full lifecycle of every task.
Files MUST never move between folders; status changes in the file itself,
keeping diffs readable. Work on tasks happens on dedicated branches; humans
review branches before merge.

Rationale: git provides audit trail, rollback, sync, and collaboration for
free — building a separate state store would duplicate it worse.

### IV. Observable Over Magical

Every step a tool takes MUST log to files — raw AI output, build output, and
test output are streamed to log files in the task folder; machine-readable
outcomes land in `result.yml`. There is no hidden state: a user MUST be able
to reconstruct what happened — and who is currently at bat — from the files
alone. When in doubt, log more; pruning (e.g., gitignoring `*.log`) is a
later optimization, silence is not.

Rationale: MagnaFlow's value is a transparent, reproducible process — a
"glass box", not a black box.

### V. AI Is a Replaceable Executor

Tools MUST NOT contain AI logic themselves; they orchestrate external tools
(Claude Code, git, build, test) as replaceable subprocesses. Prompts are
built from task files and linked specs — knowledge lives in the repo, not in
code. Swapping the AI executor MUST NOT require changes outside the
invocation layer and configuration.

Rationale: MagnaFlow's differentiator is the process, not the model; coupling
to one AI vendor would forfeit that.

### VI. Restartable and Idempotent

Every tool MUST be safe to re-run without damage: a crashed or interrupted
run leaves files in a state from which the tool (or a human) can resume or
retry. Retries are bounded (`max_attempts`) and counted (`attempts`);
`failed` is a first-class terminal status, not an exception. Tools MUST
verify preconditions (e.g., `status: pending`) before acting and skip work
already done.

Rationale: long-running AI and build steps fail routinely; recovery must be a
property of the design, not an afterthought.

### VII. Local-First, Open Formats

Everything MUST work on the developer's own machine without a cloud service
in the loop: the core process (tasks, execution, state, history) requires
only the local repository and locally installed tools. All file formats MUST
be open and vendor-neutral (Markdown, YAML, plain logs) so any editor, script,
or future tool can read and write them without MagnaFlow itself.

Rationale: local-first keeps the developer in control, works offline, and
open formats guarantee the data outlives any specific tool — including
MagnaFlow.

## Technology Constraints

- Tools are written in C#/.NET; each tool is a standalone, cross-platform
  console application.
- Preferred libraries: Spectre.Console.Cli (CLI), CliWrap (processes),
  YamlDotNet (YAML). New dependencies require a concrete need — KISS applies.
- Monorepo layout: each tool lives in `tools/<name>/` with its own solution,
  `src/`, and `tests/`; shared knowledge lives in `docs/` at the root.
- AI execution uses Claude Code headless (`claude -p`) with session reuse via
  `--resume`; authentication via the user's account (OAuth), API keys only as
  a deliberate opt-in.
- Target projects are driven through their `.magnaflow/` folder as specified
  in `docs/fase2-worker-controller/workflow-v0.1.md`.

## Development Workflow

- Features are developed spec-first with Spec Kit: specify → clarify → plan →
  tasks → implement, each feature on its own branch under `specs/`.
- Plans MUST pass the Constitution Check gate before design; violations
  require an entry in Complexity Tracking with a justification.
- Core logic of every tool MUST be covered by automated tests; tests for
  glue code (CLI wiring, process invocation) are encouraged but optional.
- Build and tests MUST run as part of every task's execution loop; failures
  feed back into bounded retries (Principle VI).
- No automatic PRs or merges: a human reviews every branch before it lands.
- Implement incrementally: make the narrowest end-to-end path work first
  (e.g., `run <task-id>` before `next` and `status`).

## Governance

This constitution supersedes other practices in this repository. All plans,
reviews, and implementations MUST verify compliance with it; deviations MUST
be justified explicitly or rejected.

Amendments are made via `/speckit-constitution`: the change is documented in
the Sync Impact Report, dependent templates are re-checked, and the version
is bumped per semantic versioning — MAJOR for removed or redefined
principles, MINOR for new principles or materially expanded guidance, PATCH
for clarifications and wording.

Compliance is reviewed at every plan's Constitution Check gate and during
branch review before merge.

**Version**: 1.1.0 | **Ratified**: 2026-07-02 | **Last Amended**: 2026-07-02
