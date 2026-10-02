# Feature Specification: Worker Controller — Plan, Questions & Feedback (v0.2)

**Feature Branch**: `002-plan-questions-feedback`

**Created**: 2026-07-08

**Status**: Draft

**Input**: User description: "Extend the existing mf-worker CLI (feature 001, built and working)
with a plan-and-questions phase and first-class feedback. Context documents:
docs/fase2-worker-controller/v0.2-direction.md (the requirements) and
specs/001-worker-controller/ (current behavior; its contracts remain valid unless this feature
amends them). What must change: (1) Before executing a task, the worker produces a short plan
derived from the task and its linked specs, and surfaces concrete questions. Questions it can
answer itself from specs, code or conventions are answered and recorded in the plan; questions
that genuinely need the human pause the task: they are written to the task folder, the task
status becomes 'questions', and the run ends. (2) The human answers by editing the task file; the
next run of that task resumes the same agent session so no context is lost, and continues from
the answers. (3) Every task run produces a readable report written for the next design
conversation: what was done, decisions taken while implementing, anything skipped or uncertain —
not just a log. (4) The worker inherits the project's own conventions (constitution/CLAUDE
guardrails) in its instruction to the agent. Out of scope: dispatcher, parallel workers, watch
mode."

## Clarifications

### Session 2026-07-08

- Q: Where should open (human-facing) questions be written for the human to answer? → A: A
  third sibling file, `NNNN-qa-name.md`, alongside the command file; the executor appends its
  questions, the human writes answers beneath each question and sets the command's status back to
  `ready`, and further rounds append to the same file. This supersedes the v0.1-derived task model
  assumed at initial drafting: v0.2 adopts the mf-spec prompt lane as the task format — flat
  numbered files in `docs/prompts/` (`NNNN-cmd-name.md`, `NNNN-qa-name.md`, `NNNN-rst-name.md`),
  not `.magnaflow/tasks/NNNN-short-name/` folders. `.magnaflow/` is now machine runtime evidence
  only (agent/build/test logs, session IDs); it holds no bookkeeping, status, or content.
- Q: Where does the plan step's output (the plan itself, plus self-answered questions) get
  recorded? → A: A dedicated fourth sibling file, `NNNN-pln-name.md`, executor-owned (like the
  report). The plan phase always runs, as a separate first agent invocation over the command and
  its linked specs. Gate: no open (human-required) questions → the controller immediately
  continues execution in the same agent session; open questions → the question is appended to the
  qa file, the command's status becomes `questions`, and the run ends without entering execution.
  After the human answers, the next run re-plans by updating the same pln file (in place, not a
  new file), then proceeds to execution if resolved. The report (rst) never repeats the plan — it
  records only what was done and any deviations from it. The command file's body is never written
  by the executor. **Amended 2026-09-03** (see FR-009): the plan itself is no longer written to a
  file at all — it is produced in the plan step's reply and stays in the agent session that
  implements from it. The pln exists only to carry open questions, so a well-specified command
  produces none; the self-answered questions move to the rst (FR-017).
- Q: What does the `aborted` status mean? → A: Any terminal non-success — build/test retries
  exhausted, human abandonment, or an unrecoverable environment error — uses the single status
  `aborted`, with the specific reason recorded in the report (rst). This matches the prompt lane's
  existing, shared definition of `aborted` ("deliberately stopped; reason in rst"). v0.1's
  `failed` becomes one of the reasons an `aborted` outcome can record.
- Q: How does the controller discover which project-convention files to include in the agent's
  instructions? → A: Fixed, well-known paths, zero configuration: `CLAUDE.md` at the repository
  root, then a constitution file at `.specify/memory/constitution.md` or `docs/constitution.md`
  (checked in that order, first hit wins); files that don't exist are silently skipped. In
  addition, the controller MUST remain strictly optional: every artifact it produces or consumes
  (cmd, pln, qa, rst, and their statuses) MUST be equally readable and writable by a human editing
  the files directly, with nothing working only through the controller — any lifecycle transition
  the controller performs must also be reproducible by hand-editing files, with identical results.
  The fully manual, interactive path (a human working a command directly in an interactive coding
  session) remains first-class and permanently supported.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Plan and self-answer before touching code (Priority: P1)

A developer has a command file (`docs/prompts/NNNN-cmd-name.md`) with status `ready`. The
controller's first act on that command, in every run, is a dedicated plan step: a separate agent
invocation over the command's own content and its linked specs, before any implementation
instruction is sent. The plan step produces its plan in its reply — not in a file — together with
every question it raised, distinguishing the ones it answered itself (from specs, existing code,
or project conventions) from the ones only the human can settle. When nothing is
genuinely open, the controller continues immediately, in the same agent session, straight into
implementation, and no pln file is written at all — nothing slows down the common case where the
command was already well-specified, and nothing is left behind that nobody reads. The
self-answered questions surface in the report instead (FR-017), where a human already looks.

**Why this priority**: This is the core value of v0.2 — surfacing the worker's reasoning and
self-resolved assumptions before code is written, without adding friction when no human input is
actually needed. Everything else in this feature builds on this gate existing.

**Independent Test**: Give the controller a `ready` command whose linked specs fully resolve any
ambiguity in its wording; run it, and verify that no pln file is created, that the run continues
into implementation and reaches a terminal status in the same invocation without pausing, and that
the rst lists the questions the agent answered itself.

**Acceptance Scenarios**:

1. **Given** a `ready` command whose goal is fully resolvable from its linked specs, existing
   code, and project conventions, **When** the controller runs it, **Then** no `NNNN-pln-name.md`
   is created, and the same run continues directly into implementation via the same agent session,
   reaching a terminal status without pausing; every question the agent asked and answered itself
   is listed in the rst when the run ends.
2. **Given** a `ready` command whose plan step raises an open question, **When** the plan step
   completes, **Then** `NNNN-pln-name.md` exists before any build or test command runs — its
   presence in the lane is itself the signal that this command needed a human.

---

### User Story 2 - Pause on genuine questions, resume after the human answers (Priority: P1)

Sometimes the plan step surfaces a question only the developer can answer. Instead of guessing,
the controller appends that question to the command's question/answer file (`NNNN-qa-name.md`),
sets the command's status to `questions`, and ends the run — no build, test, or implementation
instruction runs this attempt. The developer reads the question, writes the answer beneath it in
the same qa file, and sets the command's status back to `ready` — a plain hand-edit, the same way
every other command edit works. The next time the controller runs that command, it resumes the
exact agent session that paused (no re-explaining context), re-plans by updating the same pln file
with the new answer, and — if nothing further is open — continues into implementation through to a
terminal status.

**Why this priority**: This is the mechanism that makes it safe for the worker to run unattended on
ambiguous commands: it stops instead of guessing, and the human's answer is not wasted re-deriving
context the agent already had. Without it, "genuinely needs the human" from User Story 1 has no
safe outcome.

**Independent Test**: Give the controller a command that requires a product decision not present
in its specs; run it, verify it ends with status `questions` and the question is readable in
`NNNN-qa-name.md` without inspecting any log. Write an answer beneath the question, set status back
to `ready`, and run the same command again; verify the controller resumes the same agent session
(no repeated self-introduction of context in the agent log) and the command reaches a terminal
status.

**Acceptance Scenarios**:

1. **Given** a `ready` command whose plan step raises a question the agent cannot answer from
   specs, code, or conventions, **When** the controller runs it, **Then** the run ends with the
   command's status set to `questions`, the question is appended to `NNNN-qa-name.md`, and no
   build, test, or implementation instruction has run this attempt.
2. **Given** a command in `questions` status whose qa file now has an answer written beneath the
   question and whose status has been set back to `ready` by the developer, **When** the
   controller runs that command again, **Then** it resumes the same agent session that produced
   the pause, re-plans by updating the same pln file with the answer, and — when nothing further
   is open — proceeds into implementation.
3. **Given** a command still in `questions` status (the developer has not yet answered and reset
   it to `ready`), **When** the developer attempts to run it directly anyway, **Then** the
   controller refuses, exactly as it refuses any command whose status is not `ready` — there is no
   special-case shortcut to resume a `questions` command without the status being reset first.
4. **Given** a resumed command whose re-plan still raises a further genuine question, **When** the
   controller reaches that point, **Then** it pauses again the same way: the new question is
   appended to the same `NNNN-qa-name.md` (not a new file), and status is set back to `questions`.
5. **Given** commands in various statuses including `questions`, **When** the developer asks for
   "next" or "run-all", **Then** only commands with status `ready` are picked up — a command
   sitting in `questions` is skipped until the developer resets it to `ready`, at which point it is
   picked up like any other ready command.

---

### User Story 3 - A report the next design conversation can read (Priority: P1)

When a command's execution phase reaches a terminal outcome — `done`, or `aborted` for any
reason — the controller ensures the command's report file (`NNNN-rst-name.md`) is written or
updated: a human-readable account of what was actually done, the decisions taken while
implementing, anything skipped or uncertain, and the questions the command left open that the
executor answered for itself — so the next design conversation can pick up from it without reading
the raw agent log or the code diff. The report never restates the plan — it is a synthesis of the
outcome, not a duplicate of the intent.

**Why this priority**: The whole point of separating "talk in the thinking plane" from "execute in
the worker" is that the human never has to reconstruct what happened from raw logs — that defeats
the separation. This is as core to v0.2 as the plan/question gate.

**Independent Test**: Run a command to a terminal outcome; open `NNNN-rst-name.md` without looking
at any other file, and verify it states what was built or attempted, the decisions taken while
implementing (if any), the questions it answered for itself, and — for an aborted outcome — the
specific reason.

**Acceptance Scenarios**:

1. **Given** a command that reaches `done` in one pass with no pause, **When** the run ends,
   **Then** `NNNN-rst-name.md` is written describing what was done, the decisions taken while
   implementing, and the questions the executor answered for itself — distinct from the raw
   agent/build/test logs, and not a restatement of the plan.
2. **Given** a command whose implementation required retries after failing build or tests,
   **When** the run ends `done`, **Then** the rst reflects the final state reached (not merely the
   first attempt) and notes anything left uncertain or skipped.
3. **Given** a command that reaches `aborted` (for any reason — retries exhausted, human
   abandonment, or an unrecoverable environment error), **When** the run ends, **Then** the rst
   explicitly records the specific reason the run was aborted.
4. **Given** a command whose plan phase pauses on `questions` before entering implementation,
   **When** that run ends, **Then** no rst update occurs for that run — the pln (written for
   exactly this case) and the qa file (the open question) already carry full context for the next
   conversation, and the rst is reserved for runs whose execution phase actually ran to a terminal
   outcome.

---

### User Story 4 - The worker follows the project's own conventions (Priority: P2)

A developer has a project constitution and/or `CLAUDE.md` describing house rules. When the
controller builds the agent's instructions for any phase of a command (plan or execution), it
automatically includes whichever of these files exist, using a fixed discovery order, without the
command author having to copy them into every command or list them as linked specs.

**Why this priority**: Without this, the worker can produce work that is technically correct but
violates house rules a human session would have respected automatically — a gap developers will
keep hitting in practice. It is additive to existing prompt-building behavior, so it is lower risk
than the plan/question/report changes and can land independently.

**Independent Test**: In a target project with a `CLAUDE.md` and/or a constitution file, run any
command and inspect the instructions actually sent to the agent (e.g., via the agent log's
recorded prompt); verify the convention content is present without having been listed in the
command's own `specs:` list.

**Acceptance Scenarios**:

1. **Given** a target project with `CLAUDE.md` at its repository root, **When** the controller
   prepares any phase's instructions, **Then** its content is included automatically.
2. **Given** a target project with a constitution file at `.specify/memory/constitution.md` or, if
   absent there, at `docs/constitution.md`, **When** the controller prepares any phase's
   instructions, **Then** the first one found (in that order) is included automatically.
3. **Given** a target project with neither file present, **When** the controller prepares a
   command's instructions, **Then** it proceeds exactly as in v0.1, without erroring over the
   absence.

---

### Edge Cases

- **Qa file garbled while being answered** (e.g., malformed structure, question/answer pairing
  broken): treated like any other malformed file — reported clearly, command left untouched, no
  guessing, no status change.
- **Command reset to `ready` but the paused agent session is no longer resumable** (e.g., the
  session evidence in `.magnaflow/` is missing or expired): the controller reports this clearly as
  an environment/usage error before making any change, rather than silently starting a fresh
  session and losing the pause context.
- **A command names an explicit `resume:` target while also having its own pause history**:
  explicit `resume:`/`group` continuity (v0.1 FR-013a) takes precedence over the default
  self-resume behavior of FR-012/FR-013 below.
- **Plan-phase agent invocation itself crashes** (before producing a plan): treated the same as any
  other agent crash in v0.1 — it counts as a failed attempt against the command's `max_attempts`
  and normal retry handling applies; a clean pause for genuine human questions, by contrast, never
  consumes an attempt (it is a deliberate stop, not a failure).
- **A command paused on `questions` is never revisited**: it stays in `questions` indefinitely;
  nothing times it out, and it remains invisible to "next"/"run-all" the same way any non-`ready`
  status is.
- **Convention files are very large**: included in full — the same "no half-built prompts"
  principle that already governs linked specs in v0.1; the controller does not summarize or
  truncate on the agent's behalf.
- **A human manually abandons a command** by hand-editing its status to `aborted` and writing
  their own reason directly into the rst: a later controller run encountering that command MUST
  treat it exactly as if the controller itself had aborted it (per the manual-parity guarantee),
  i.e., as a terminal, non-runnable state.
- **Orphaned pln/qa/rst file** (a sibling file exists in `docs/prompts/` without a matching
  `NNNN-cmd-name.md`): reported as a malformed/inconsistent state and skipped, the same way v0.1
  reports other malformed task data, since the cmd file is the anchoring identity for the set.

## Requirements *(mandatory)*

### Functional Requirements

#### Command discovery and the prompt-lane file model

- **FR-001**: The controller MUST treat each `NNNN-cmd-name.md` file directly under `docs/prompts/`
  as one command; `NNNN` (a numeric prefix) plus `name` together form the command's identity and
  MUST be shared, verbatim, by its optional sibling files: `NNNN-pln-name.md` (plan),
  `NNNN-qa-name.md` (question/answer dialogue), and `NNNN-rst-name.md` (report). Only the cmd file
  is mandatory; the others are created only when their phase produces one.
- **FR-002**: The controller MUST order commands lexicographically by their `NNNN` prefix; this
  order is the execution order for "next" and "run-all".
- **FR-003**: The controller MUST read command metadata from the cmd file's YAML frontmatter:
  title, status (`draft | ready | running | questions | done | aborted`), and the same optional
  fields as v0.1 (work branch, base branch, group, fresh-session flag, linked spec paths, attempts
  used, maximum attempts) — all remaining optional, as in v0.1, with `attempts` defaulting to `0`.
- **FR-004**: `docs/prompts/` (cmd, pln, qa, rst) is the sole bookkeeping surface for a command's
  content, plan, dialogue, and report; `.magnaflow/` MUST hold only machine runtime evidence for a
  command — agent, build, and test logs, and the agent session ID(s) associated with it — and MUST
  NOT hold status, content, or any other bookkeeping data.
- **FR-005**: The command file's body (goal, context, acceptance criteria) MUST never be written by
  the agent; it is authored and edited only by the human. The controller MUST only ever rewrite the
  `status:` and `attempts:` values in the cmd's frontmatter, in place — identical to v0.1's write
  rule, now applied to the cmd file.

#### Single-command execution (run)

- **FR-006**: The controller MUST execute a command only when its status is `ready`; any other
  status (`draft`, `running`, `questions`, `done`, `aborted`) results in a refusal with an
  explanatory message and no changes — this amends v0.1 FR-004, renaming `pending` to `ready` and
  explicitly confirming that `questions` is never directly runnable without first being reset to
  `ready` by the human.
- **FR-006a**: The controller MUST refuse to start any command execution while the working tree
  has uncommitted changes (v0.1 FR-004a, unchanged), and MUST refuse while the currently checked
  out branch is a command's own work branch (v0.1 FR-006a, unchanged).
- **FR-007**: When the cmd frontmatter names a work branch, the controller MUST perform the
  command's work on that branch (v0.1 FR-006, unchanged in mechanics, now sourced from the cmd
  file instead of `task.md`).

#### Plan phase (always runs first)

- **FR-008**: For every run of a `ready` command, the controller MUST first invoke the agent in a
  dedicated plan step — a separate invocation using the cmd body, its linked specs, and the
  project's conventions (FR-019/FR-020) as input — before any build, test, or implementation
  instruction is sent.
- **FR-009**: The plan step MUST produce its plan in its own reply, inside the session that goes
  on to implement it — not in a file. It MUST write the command's pln file (`NNNN-pln-name.md`)
  only when the round has at least one open (human-required) question, or when a pln or qa file
  for that command already exists (a re-plan, FR-014). A run with nothing open and no earlier
  pln/qa MUST leave no pln behind; the questions the agent answered itself (from linked specs,
  existing code, or project conventions), together with the answers given, are recorded in the rst
  instead (FR-017).
- **FR-010**: When the plan step raises no open (human-required) question, the controller MUST
  continue immediately, within the same run and the same agent session, into the execution phase —
  the plan step MUST NOT itself require a human pause when nothing genuinely requires one.
- **FR-011**: When the plan step raises at least one open question, the controller MUST NOT guess
  and MUST NOT enter the execution phase: it MUST append the question(s) to the command's qa file
  (`NNNN-qa-name.md`), set the command's status to `questions`, and end the run without invoking
  any build or test command.
- **FR-011a**: A clean pause for open questions (FR-011) MUST NOT increment the command's
  `attempts` counter — only agent crashes and build/test repair retries (FR-016) do, since a pause
  is a deliberate stop waiting on the human, not a failure.

#### Resume after the human answers

- **FR-012**: The human answers a paused command by writing the answer directly beneath the
  relevant question in the qa file, then setting the command's frontmatter status back to `ready`
  — both by hand-editing plain text; the controller MUST NOT require or provide any other
  mechanism for recording an answer.
- **FR-013**: When a command whose status was `questions` is run again after being reset to
  `ready`, the controller MUST resume the same agent session that produced the pause (identified
  via the session evidence retained in `.magnaflow/`), rather than starting a fresh session —
  unless the command's frontmatter names an explicit `resume:` target or matching `group`
  continuity applies, in which case existing v0.1 precedence rules (v0.1 FR-013a) govern instead.
- **FR-014**: On such a resumed run, the plan step MUST run again (re-plan), incorporating any
  newly available answers from the qa file, and MUST rewrite the same pln file in place — not
  create a new pln file, and not merely append a disconnected new plan — before the gate
  (FR-010/FR-011) is re-evaluated. When the re-plan resolves everything, the rewritten pln MUST
  NOT retain the previous round's open-questions section: a stale section would re-trigger the
  pause (FR-011) on every further run and the command would never be implemented.
- **FR-015**: A re-plan MAY itself raise a further open question; when it does, the controller
  MUST pause again exactly as in FR-011, appending the new question(s) to the same qa file rather
  than creating a new one — pausing is not a one-time allowance.

#### Execution and the report

- **FR-016**: Once the plan-step gate passes (FR-010, in the initial run or after a resume), the
  controller MUST execute the command exactly as in v0.1: running the project's build and test
  commands with bounded, counted retries (v0.1 FR-009/FR-010), capturing output to logs in
  `.magnaflow/`.
- **FR-017**: When a run's execution phase reaches a terminal outcome (`done` or `aborted`), the
  controller MUST ensure the command's rst file (`NNNN-rst-name.md`) is written or updated with a
  human-readable account of what was done, the decisions taken while implementing, anything
  skipped or uncertain, and the questions the command left open that the executor answered for
  itself (with the answers it settled on) — the rst MUST NOT restate the plan.
- **FR-018**: `aborted` is a single status covering every terminal non-success outcome — build/test
  retries exhausted, human abandonment, or an unrecoverable environment error; when a run ends
  `aborted`, the rst MUST record the specific reason.
- **FR-019**: A run whose plan phase ends in a pause (`questions`, FR-011) MUST NOT write or update
  the rst for that run — the pln, which exists precisely for a paused run, and the qa file already
  carry sufficient context; the rst is reserved for runs whose execution phase actually reached a
  terminal outcome.

#### Convention inheritance

- **FR-020**: When preparing agent instructions for any phase of a command (plan or execution), the
  controller MUST automatically include project convention content found via this fixed discovery
  order, with no configuration required: (1) `CLAUDE.md` at the repository root; (2) a constitution
  file, checked at `.specify/memory/constitution.md`, then `docs/constitution.md`, first hit wins.
- **FR-021**: A file absent from either check MUST NOT be an error; the controller proceeds without
  it, exactly as in v0.1's handling of optional inputs.

#### Manual-parity guarantee

- **FR-022**: The controller MUST NOT be the only way to produce, consume, or transition any
  artifact under `docs/prompts/` or `.magnaflow/`. Every lifecycle transition the controller
  performs (`ready → running → {questions | done | aborted}`, and the human's own `questions →
  ready`) MUST be reproducible by a human directly hand-editing the relevant plain-text files, with
  results indistinguishable from the controller having performed them.
- **FR-023**: The fully manual, interactive path — a human working a command's content directly in
  an interactive coding session and writing its pln/qa/rst by hand — MUST remain first-class and
  permanently supported; the controller is optional automation layered over this workflow, never a
  replacement it depends on.

#### Queue commands

- **FR-024**: "Next" MUST scan `docs/prompts/` for commands with status `ready`, select the one
  with the lowest `NNNN`, and execute it as a single-command run (FR-006 applies); if none is
  `ready`, it MUST exit cleanly with an informative message.
- **FR-025**: "Run-all" MUST execute all `ready` commands sequentially in `NNNN` order until none
  remain, then stop; a command that ends `aborted` MUST NOT stop the batch.
- **FR-026**: "Status" MUST print a read-only overview of every command (ID, title, status,
  attempts) in `NNNN` order without modifying or committing anything.

### Key Entities

- **Command (cmd)**: `docs/prompts/NNNN-cmd-name.md` — one unit of delegated work; human-authored
  body (goal, context, acceptance criteria), YAML frontmatter for status and the same optional
  fields as v0.1's task (branch, base, group, fresh-session flag, linked specs, attempts,
  max-attempts). Never written by the agent; only its `status`/`attempts` fields are written by the
  controller.
- **Plan (pln)**: `NNNN-pln-name.md` — executor-owned; written only for a round that has an open
  question (or to rewrite one an earlier round left behind), carrying the intended approach and the
  `## Open questions (this round)` section the controller reads at the gate. A well-specified
  command has no pln at all, and the file's presence in the lane means this command needed a
  human. Rewritten in place on re-plan, not duplicated per attempt.
- **Question/Answer dialogue (qa)**: `NNNN-qa-name.md` — created only when a genuine open question
  arises; the executor appends questions, the human appends answers beneath them; further rounds of
  pausing append to the same file rather than creating new ones.
- **Report (rst)**: `NNNN-rst-name.md` — executor-owned; written or updated only when a run's
  execution phase reaches a terminal outcome. Synthesizes what was done, the decisions taken while
  implementing, anything skipped or uncertain, and the questions the executor answered for itself —
  never a repeat of the plan — and, for `aborted`, the specific reason.
- **Command Status** (extended from v0.1): `draft → ready → running → {questions ⇄ ready} → done |
  aborted`. `draft` is a human-authoring-only state the controller never transitions into or out
  of. `questions` is non-terminal and requires the human to reset status to `ready` before the
  controller acts on it again — never runnable directly.
- **Machine Runtime Evidence**: per-command content under `.magnaflow/` — agent/build/test logs and
  the agent session ID(s); the only content `.magnaflow/` now holds, since bookkeeping moved to
  `docs/prompts/`.
- **Project Conventions**: `CLAUDE.md` (repository root) and/or a constitution file (fixed discovery
  order), included automatically in every phase's agent instructions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For any command run, a developer can tell — from `docs/prompts/` alone, without
  reading the agent log — whether the agent guessed on an open question or genuinely paused for
  one: a pln file exists exactly for the commands that paused, and the rst lists what the executor
  answered for itself. Zero runs guess past a question the agent itself flagged as unanswerable.
- **SC-002**: A developer who answers a paused command's question and resets it to `ready` never
  has to restate context the agent already had before pausing — the resumed run's agent log shows
  continuation, not a repeated self-introduction.
- **SC-003**: For any command whose execution phase completed, a developer can start or resume a
  design conversation using only `NNNN-rst-name.md` — what was done, decisions made, and open
  uncertainties — without first reading the code diff.
- **SC-004**: A developer working in a project with house conventions never has to copy those
  conventions into a command to have the agent respect them.
- **SC-005**: Commands whose ambiguity is fully resolvable from existing specs/code/conventions
  complete in the same run as v0.1 (no added pause), preserving today's zero-touch experience for
  well-specified commands.
- **SC-006**: For any lifecycle transition or artifact the controller would normally produce, a
  developer can instead hand-edit the plain-text files to the same effect, and neither the
  controller nor a later reader of `docs/prompts/`/`.magnaflow/` can distinguish the two paths.

## Assumptions

- **Amends, does not replace, v0.1 contracts**: build/test-gated success, retries, branch handling,
  and the shape of the queue commands from `specs/001-worker-controller/` remain valid; this
  feature replaces the task-folder file model with the `docs/prompts/` prompt lane, extends the
  status lifecycle, and adds the plan/question/report artifacts and convention inheritance on top.
- **Plan phase is separate from the build/test retry loop**: a clean pause for genuine human
  questions never counts as an `attempts` use; only agent crashes and build/test repair retries do
  (FR-011a).
- **Question rounds are unbounded by design, not by omission**: unlike build/test retries (bounded
  by `max_attempts`), pausing on genuine human questions has no round limit, since each pause is
  waiting on a human action, not retrying a failure.
- **No new CLI surface beyond running a command by ID** (already existing in v0.1); resuming a
  paused command is simply running it again after the human has reset its status to `ready` by
  hand — not a distinct command-line mode.
- **`draft` is a human-authoring-only status**: the controller never transitions a command into or
  out of `draft`; it only ever acts on `ready` commands (and only ever sets `running`,
  `questions`, `done`, or `aborted`).
- **Out of scope** (explicit in the source direction document): a dispatcher or poll agent,
  parallel/concurrent workers, and watch mode remain out of scope, unchanged from v0.1's own
  "deliberately not" list.
