# Feature Specification: MagnaFlow Worker Controller (v0.1)

**Feature Branch**: `001-worker-controller`

**Created**: 2026-07-02

**Status**: Draft

**Input**: User description: "MagnaFlow Workflow v0.1 (pinned for the Worker Controller) — a one-shot
controller that scans `.magnaflow/tasks/` in a target project, picks up pending tasks, has an AI
coding agent implement them, runs build and tests with bounded retries, and writes all status,
logs, and results back as committed plain-text files."

## Clarifications

### Session 2026-07-02

- Q: How much autonomy does the headless AI agent get during unattended execution? → A:
  Configurable per project in the project configuration; safe default, full autonomy
  (e.g., skipping per-action permission prompts) as an explicit opt-in.
- Q: What is a task's work branch created from? → A: Configurable per task via an optional
  `base:` frontmatter field; when absent, the repository's default branch. Tasks that build on
  an unmerged predecessor name that predecessor's branch as their base.
- Q: What happens when the working tree is dirty at the start of a run? → A: The controller
  refuses to start and tells the developer to commit or stash first; it never mixes human
  changes into a task's commits.
- Q: What defines a task as successfully "done"? → A: Build and tests passing. Acceptance
  criteria in the task file guide the agent and the human reviewer; machine verification of
  acceptance criteria is deliberately left to a future separate tool.
- Q (post-v0.1 dogfood, 2026-07-02): What if the developer invokes the controller while a
  task's work branch is checked out? → A: Refuse (read-only "status" warns instead): a work
  branch carries a frozen mid-run snapshot of the task state, so the queue would be misread
  and orchestration commits would pollute the work branch.
- Q (post-v0.1 dogfood, 2026-07-02): Must every task have a work branch? → A: No. `branch:`
  is optional; without it the task runs branchless and the work is committed directly on the
  invoking branch. `base:` is only valid together with `branch:`.
- Q (post-v0.1 dogfood, 2026-07-03): Can agent context be continued across controller
  invocations, not just within one batch? → A: Yes, explicitly: every run records the agent
  session ID it ended with in its result file, and a task's `resume:` frontmatter field
  continues either a raw session ID (used verbatim) or another task's recorded session (when
  the value matches the task-ID pattern). Explicit `resume:` wins over group continuity;
  combining it with `fresh_session: true` is rejected as contradictory.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run a single task end-to-end (Priority: P1)

A developer has described a unit of work as a task folder (`.magnaflow/tasks/0001-short-name/`
containing `task.md` with goal, context, and acceptance criteria). They start the controller for
that specific task ID. The controller marks the task as running, creates the task's work branch,
builds an instruction prompt from the task content and its linked spec files, hands it to the AI
coding agent, then runs the project's build and test commands. If build or tests fail, the failure
output is fed back to the agent for a bounded number of repair attempts. When finished, all
changes are committed on the task branch, a machine-readable result file is written, and the
task's status is set to `done` or `failed`. The developer can then review the branch.

**Why this priority**: This is the entire value proposition — delegating one well-described task
to an AI executor with an auditable, hands-off execution loop. Every other command is a thin
layer over this capability.

**Independent Test**: In a target project with one pending task, run the controller with that
task ID and verify: status transitions are committed, the branch exists with the work committed,
the task folder contains the agent log, build log, test log, and result file, and the final
status matches the outcome.

**Acceptance Scenarios**:

1. **Given** a task with `status: pending`, **When** the controller runs it and the agent's work
   passes build and tests on the first attempt, **Then** the task folder contains agent, build,
   and test logs plus a result file reporting `done` with 1 attempt, the frontmatter status is
   `done`, and all work is committed on the task's branch.
2. **Given** a task whose first attempt fails the tests, **When** the controller feeds the test
   failure output back to the agent and the second attempt passes, **Then** the result file
   reports `done` with 2 attempts and the logs show both attempts.
3. **Given** a task that still fails after the configured maximum number of attempts, **When**
   the controller gives up, **Then** the frontmatter status and result file report `failed`, the
   attempt count equals the configured maximum, and all logs of every attempt are preserved.
4. **Given** a task whose status is not `pending` (e.g., `done` or `running`), **When** the
   controller is asked to run it, **Then** it refuses with a clear message and changes nothing.
5. **Given** a task ID that does not exist, **When** the controller is asked to run it, **Then**
   it reports the error clearly and changes nothing.

---

### User Story 2 - Pick up the next pending task (Priority: P2)

A developer (or, later, a dispatcher tool) asks the controller for "the next task". The
controller scans all task folders, sorts them by folder name (the four-digit numeric prefix
defines execution order), picks the first task with status `pending`, and executes it exactly as
in User Story 1. If no task is pending, it says so and exits cleanly.

**Why this priority**: Removes the need to know task IDs; it is the building block the future
dispatcher will call. Depends entirely on User Story 1's execution loop.

**Independent Test**: In a project with tasks 0001 (`done`), 0002 (`pending`), 0003 (`pending`),
run "next" and verify task 0002 is executed and 0003 is untouched.

**Acceptance Scenarios**:

1. **Given** several tasks of which the lowest-numbered pending one is 0002, **When** the
   controller runs "next", **Then** only task 0002 is executed.
2. **Given** no pending tasks, **When** the controller runs "next", **Then** it reports that
   nothing is pending and exits without changing any file.

---

### User Story 3 - Work through all pending tasks (Priority: P3)

A developer starts a batch run before stepping away. The controller executes all pending tasks
one at a time, in folder-name order, and then stops — it never waits for new tasks to appear.
A task that fails does not stop the batch; the controller moves on to the next pending task.

**Why this priority**: Convenience layering over Stories 1 and 2; valuable for overnight or
lunch-break batches but not required to prove the core loop.

**Independent Test**: With three pending tasks, run "run-all" and verify all three were executed
in order and the controller exited afterwards.

**Acceptance Scenarios**:

1. **Given** tasks 0002, 0003, 0004 pending, **When** the controller runs "run-all", **Then**
   they are executed sequentially in that order and the controller stops when none are pending.
2. **Given** task 0003 ends as `failed` during a batch, **When** the batch continues, **Then**
   task 0004 is still executed and the summary reports both outcomes.

---

### User Story 4 - See the state of the queue (Priority: P3)

A developer wants a quick overview: which tasks exist, what status each has, and how many
attempts have been used. The controller prints a read-only summary from the task files without
modifying anything.

**Why this priority**: Pure convenience — the same information is visible in the files
themselves (glass-box principle), but a one-command overview saves time.

**Independent Test**: With a mix of `pending`, `running`, `done`, and `failed` tasks, run
"status" and verify the overview matches the frontmatter of every task file and nothing changed.

**Acceptance Scenarios**:

1. **Given** tasks in various statuses, **When** the controller shows status, **Then** every
   task appears with its ID, title, status, and attempts used, in folder-name order.
2. **Given** any project state, **When** the controller shows status, **Then** no file is
   created, modified, or committed.

### Edge Cases

- **Task stuck in `running`** (controller crashed or was interrupted mid-run): the task is not
  picked up by "next"/"run-all"; running it directly is refused with a message telling the human
  to inspect and manually reset the status. No automatic recovery in v0.1.
- **Malformed task file** (missing/invalid frontmatter, unknown status value): the controller
  reports the file and the problem, skips the task, and continues; it never guesses.
- **Linked spec file missing**: the controller fails the task's preparation step with a clear
  message before invoking the agent (no half-built prompts).
- **Missing or incomplete project configuration** (no config file, no build/test commands): the
  controller refuses to run tasks and explains exactly what is missing.
- **AI agent cannot be started** (not installed, not authenticated): agent availability is
  verified as a precondition before any status change, so the task stays `pending` and the run
  is refused as an environment error. If the agent starts but crashes mid-run, that counts as
  a failed attempt (underlying error preserved in the agent log) and normal retry/`failed`
  handling applies.
- **Work branch already exists**: the controller reuses/continues that branch rather than
  failing (supports re-running after a failed attempt).
- **Base branch does not exist** (`base:` names an unknown branch): the run is refused before
  any status change, with a clear message.
- **Dirty working tree at start**: the run is refused before any status change; the developer
  commits or stashes their own changes first.
- **Controller invoked from a task's work branch** (e.g. after checking it out for review):
  refused before any status change, naming the owning task and how to switch back (FR-006a);
  the status overview shows its table with a frozen-snapshot warning.
- **Empty task queue**: "next" and "run-all" exit cleanly with an informative message.
- **Build or tests produce no output or hang**: command output (or its absence) is still logged;
  a configurable timeout bounds each external command. [Timeout default: see Assumptions]

## Requirements *(mandatory)*

### Functional Requirements

#### Task discovery and identity

- **FR-001**: The controller MUST treat each direct subfolder of `.magnaflow/tasks/` containing
  a `task.md` as one task; the folder name (`NNNN-short-name`, four digits with leading zeros)
  is the task's unique ID and MUST NOT be duplicated inside the file.
- **FR-002**: The controller MUST order tasks lexicographically by folder name; this order is
  the execution order for "next" and "run-all".
- **FR-003**: The controller MUST read task metadata from the YAML frontmatter of `task.md`:
  title, status (`pending | running | done | failed`), optional work branch name, optional
  base branch (only valid together with a work branch), optional group, optional
  fresh-session flag, linked spec file paths, attempts used, and maximum attempts.

#### Single-task execution (run)

- **FR-004**: The controller MUST execute a task only when its status is `pending`; any other
  status results in a refusal with an explanatory message and no changes.
- **FR-004a**: The controller MUST refuse to start any task execution while the working tree
  has uncommitted changes, with a message telling the developer to commit or stash first;
  human changes are never mixed into a task's commits. (Read-only "status" is exempt.)
- **FR-005**: The controller MUST record every status transition by updating the frontmatter in
  place (files never move) and committing that change, so version history shows the full
  lifecycle of the task.
- **FR-006**: When the task's frontmatter names a work branch, the controller MUST perform
  the task's work on that branch, creating it if it does not exist and reusing it if it does.
  A new branch is created from the branch named in the task's optional `base:` frontmatter
  field; when no base is given, from the repository's default branch. A missing base branch
  is an error reported before any work starts. When the frontmatter names no work branch,
  the task runs branchless: the work is committed directly on the invoking branch, as its
  own commit, separate from the status commits.
- **FR-006a**: The controller MUST refuse to execute any task while the currently checked-out
  branch is the work branch of any task (exit: precondition refusal, with a message naming
  the owning task and the command to switch back). Rationale: a work branch carries a frozen
  mid-run snapshot of `.magnaflow/`, so queue reads there are wrong and writes would pollute
  the reviewable branch. The read-only status overview MUST warn instead of refuse.
- **FR-007**: The controller MUST build the agent's instruction prompt from the task file's body
  plus the full content of each linked spec file, and MUST abort preparation with a clear error
  if a linked file is missing.
- **FR-008**: The controller MUST run the AI coding agent non-interactively (headless) and
  stream its raw output to an agent log file inside the task folder as it happens.
- **FR-008a**: The agent's autonomy level (how freely it may edit files and run commands
  without per-action approval) MUST be configurable per project in the project configuration
  and passed through to the agent. The default is the agent's own safe behavior; full
  autonomy is an explicit opt-in by the project owner.
- **FR-009**: The controller MUST run the project's build and test commands as defined in the
  project configuration file, capturing their output to separate build and test log files in the
  task folder. Build and tests passing is the sole machine-checked success condition for a
  task; acceptance criteria in the task file are guidance for the agent and the human
  reviewer, not verified by the controller.
- **FR-010**: On build or test failure, the controller MUST feed the failure output back to the
  agent as a follow-up instruction, increment the attempt counter, and retry — up to the task's
  configured maximum attempts.
- **FR-011**: After the final attempt (success or exhaustion), the controller MUST commit the
  work on the task branch, write a machine-readable result file (`result.yml`) in the task
  folder containing at minimum: task ID, final status, attempts used, start and finish
  timestamps, the agent session ID the run ended with (when the agent reported one), and a
  one-paragraph summary — and set the frontmatter status to `done` or `failed` accordingly,
  committing the result.

#### Agent session policy

- **FR-012**: Within a single task, follow-up instructions (retries) MUST continue the same
  agent session so context is preserved.
- **FR-013**: When the immediately preceding executed task within the same controller
  invocation (e.g., a batch run) has the same group value, the controller MUST continue that
  task's agent session; a new group, no group, or an explicit fresh-session flag MUST start a
  new session. Implicit (group-based) session identity is not persisted between invocations;
  cross-invocation continuation is explicit via FR-013a.
- **FR-013a**: A task MAY name an explicit session to continue via a `resume:` frontmatter
  field: a value matching the task-ID pattern refers to another task, whose recorded session
  (from its result file) is continued; any other value is treated as a raw agent session ID
  and passed through verbatim. Explicit resume takes precedence over group continuity. A
  reference to a task that does not exist, has no result file, or recorded no session is a
  usage error reported before any mutation. Combining `resume:` with `fresh_session: true`
  makes the task malformed.

#### Queue commands

- **FR-014**: "Next" MUST scan all tasks, select the first pending one in folder-name order,
  and execute it as a single-task run; if none is pending it MUST exit cleanly with an
  informative message.
- **FR-015**: "Run-all" MUST execute pending tasks sequentially in folder-name order until none
  remain, then stop; a failed task MUST NOT stop the batch.
- **FR-016**: "Status" MUST print a read-only overview of all tasks (ID, title, status,
  attempts) in folder-name order without modifying or committing anything.

#### Behavioral principles (from the constitution)

- **FR-017**: The controller MUST be one-shot: it performs the requested work and exits; it
  MUST NOT poll, wait for new tasks, or schedule future work.
- **FR-018**: The controller MUST be restartable: an interrupted run leaves only committed,
  inspectable file state behind, and re-running any command MUST never corrupt task data or
  redo completed work.
- **FR-019**: The controller MUST NOT contain AI-specific logic beyond invoking the external
  agent command and relaying text; swapping the agent MUST be a configuration-level change.
- **FR-020**: All state the controller produces (status, logs, results) MUST be plain-text
  files inside the task folder; the controller MUST NOT keep hidden state elsewhere.
- **FR-021**: The controller MUST signal the outcome of its run through its process exit code
  so that composing tools (the future dispatcher) can react without parsing text output.

### Key Entities

- **Task**: One unit of delegated work. Lives as a folder (`NNNN-short-name`) whose name is its
  ID and ordering key; defined by `task.md` (YAML frontmatter for machine-read metadata,
  Markdown body with goal, context, and acceptance criteria for the agent).
- **Task Status**: `pending → running → done` with `running → failed` after exhausted attempts.
  Deliberately minimal; spec-layer statuses (proposed, reviewing, …) are out of scope.
- **Project Configuration** (`.magnaflow/config.yml` in the target project): build command, test
  command, retry limit defaults, and agent autonomy/permission settings — the contract between
  a target project and any controller.
- **Execution Result** (`result.yml` per task): machine-readable outcome — task ID, final
  status, attempts, start/finish times, human-readable summary.
- **Execution Logs** (per task): raw agent output, build output, test output — the audit trail.
- **Agent Session / Group**: continuity of agent context. Tasks sharing a group that run
  directly after one another share one session; otherwise each task gets a fresh one.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can delegate a well-described task and get a reviewable result (branch
  plus result file) with zero manual intervention between starting the run and reviewing it.
- **SC-002**: For any executed task, a person can reconstruct what happened — instructions given,
  agent output, build/test outcomes, attempts, duration, final status — from the files in the
  repository alone, without asking the controller or any other tool.
- **SC-003**: 100% of task status transitions are visible in version history as file diffs.
- **SC-004**: After a simulated interruption at any point of a run, the project is left in a
  state a human can diagnose from the files alone, and no completed task is ever re-executed.
- **SC-005**: A batch run over multiple pending tasks completes without human input and ends
  with every task in a terminal state (`done` or `failed`) and a result file each.
- **SC-006**: The queue overview reflects the exact content of the task files at the moment of
  invocation (zero drift possible, since the files are the only source).

## Assumptions

- **Execution environment**: the controller runs on the developer's machine inside a target
  project that is a git repository with a `.magnaflow/` folder; git and the AI agent command are
  installed and authenticated (local-first, per the constitution).
- **Current AI executor**: Claude Code in non-interactive mode is the v0.1 agent; per the
  constitution it is replaceable and nothing in the requirements depends on which agent runs.
- **Commit granularity**: the branch base is defined (FR-006: `base:` field or default
  branch); which branch carries the status-transition commits (FR-005, FR-011) remains a design
  decision for the plan phase, as long as FR-005's "full lifecycle visible in history" holds.
- **Stuck `running` tasks** are a human problem in v0.1: no locking, no takeover, no automatic
  reset (single developer, single controller instance assumed — no parallel runs).
- **Command timeouts**: external commands (agent, build, tests) get a generous default timeout
  (order of tens of minutes, configurable in the project configuration) so a hung command
  cannot block a batch forever.
- **Task authoring** is out of scope: tasks are written by hand (or by Claude in chat) before
  the controller runs; the controller never creates or edits task definitions beyond the
  frontmatter status/attempt fields.
- **Out of scope for v0.1** (explicit in the source document): dispatcher/poll agent, locking
  and parallel workers, remote/distributed workers, dashboard, runtime bridge, spec worker,
  automatic PRs or merges — a human reviews every task branch. Machine verification of
  acceptance criteria is also out of scope: it belongs to a future separate reviewer tool.
- **Scale**: up to 9999 tasks per project by construction of the numbering scheme; typical
  projects are expected to stay far below that.
