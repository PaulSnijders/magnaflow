---

description: "Task list for MagnaFlow Worker Controller (v0.1)"
---

# Tasks: MagnaFlow Worker Controller (v0.1)

**Input**: Design documents from `/specs/001-worker-controller/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Core-logic tests are MANDATORY per the constitution (scanner, frontmatter surgery,
config, prompt builder, run loop, result writer). Process glue (CliWrap runner, real
GitClient, Claude invocation) is validated via quickstart scenarios instead.

**Organization**: Tasks grouped by user story from spec.md. US1 (`run`) is the MVP; US2–US4
are thin layers over it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 = run single task, US2 = next, US3 = run-all, US4 = status

## Path Conventions

Everything lives in `tools/worker-controller/`; `SRC` = `tools/worker-controller/src/MagnaFlow.WorkerController`, `TESTS` = `tools/worker-controller/tests/MagnaFlow.WorkerController.Tests`.

---

## Phase 1: Setup

**Purpose**: Compilable skeleton matching plan.md's structure

- [X] T001 Create solution and projects: `tools/worker-controller/WorkerController.sln`, console app `SRC/MagnaFlow.WorkerController.csproj` (net10.0, packages Spectre.Console.Cli, CliWrap, YamlDotNet), test project `TESTS/MagnaFlow.WorkerController.Tests.csproj` (xunit, project reference); verify `dotnet build` and `dotnet test` pass
- [X] T002 Wire Spectre.Console.Cli CommandApp in `SRC/Program.cs` with four stub commands (`run <task-id>`, `next`, `run-all`, `status`), global `--project <path>` option defaulting to cwd, and `SRC/ExitCodes.cs` constants (0/1/2/3/4 per contracts/cli.md)

**Checkpoint**: `mf-worker status` runs and prints a stub — CLI surface exists

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: File-format core and process seams every story depends on

- [X] T003 [P] Implement `SRC/Tasks/TaskStatus.cs` (enum + parse) and `SRC/Tasks/TaskFile.cs`: frontmatter split on `---` fences, YamlDotNet read of all fields per data-model.md, body preserved verbatim, and surgical in-place update of only `status:`/`attempts:` lines (research R4)
- [X] T004 [P] Implement `SRC/Tasks/TaskScanner.cs`: enumerate `.magnaflow/tasks/*/task.md`, validate folder-name pattern `^\d{4}-[a-z0-9-]+$`, sort lexicographically, surface malformed tasks as warnings without crashing (FR-001/002, edge case)
- [X] T005 [P] Implement `SRC/Config/ProjectConfig.cs`: load `.magnaflow/config.yml` per contracts/file-formats.md, apply defaults (max_attempts 3, timeout 30 min, agent `claude`, args []), fail with clear messages listing missing required fields (build/test commands)
- [X] T006 [P] Implement `SRC/Infrastructure/IProcessRunner.cs` + `SRC/Infrastructure/CliWrapProcessRunner.cs`: run shell command in working dir, stream stdout+stderr lines to a callback (for log files + console mirror), enforce CancellationToken timeout, return exit code (research R3)
- [X] T007 Implement `SRC/Infrastructure/GitClient.cs` on IProcessRunner: `IsWorkingTreeClean` (status --porcelain), `BranchExists`, `CurrentBranch`, `DefaultBranch`, `Checkout`, `CreateBranchFrom`, `StagePaths`/`StageAllExcept(.magnaflow)`, `Commit` (research R6 primitives)
- [X] T008 [P] Unit tests for TaskFile in `TESTS/TaskFileTests.cs`: parse all frontmatter fields, defaults, unknown status ⇒ malformed, surgical update preserves comments/formatting/body byte-for-byte outside the two target lines
- [X] T009 [P] Unit tests for TaskScanner in `TESTS/TaskScannerTests.cs`: ordering, pattern rejection, malformed-file warning path (temp directories)
- [X] T010 [P] Unit tests for ProjectConfig in `TESTS/ProjectConfigTests.cs`: full config, defaults-only, missing build/test command errors

**Checkpoint**: File formats readable/writable and provable — user story implementation can begin

---

## Phase 3: User Story 1 - Run a single task end-to-end (Priority: P1) 🎯 MVP

**Goal**: `mf-worker run <task-id>` executes the full loop: preconditions → running commit →
work branch → agent → build/test → bounded retries → work commit → result.yml + terminal
status commit (two-plane model, research R6)

**Independent Test**: quickstart.md Scenarios 1–3 (happy path, refusals mutate nothing,
bounded retries to `failed`)

### Implementation for User Story 1

- [X] T011 [P] [US1] Implement `SRC/Execution/PromptBuilder.cs`: task body + full content of each `specs:` file, error listing any missing paths (FR-007), fixed preamble instructing the agent to never touch `.magnaflow/` and never run git commit (research R6); follow-up prompt variant that wraps build/test failure output (FR-010)
- [X] T012 [P] [US1] Implement `SRC/Agents/IAgentRunner.cs` + `SRC/Agents/ClaudeCodeRunner.cs`: invoke `agent.command` with `-p <prompt> --output-format stream-json --verbose` plus verbatim `agent.args`, stream raw lines to claude.log via callback, capture session ID from init event, support `--resume <session-id>` for follow-ups, extract final response text for the result summary (research R5)
- [X] T013 [P] [US1] Implement `SRC/Execution/ResultWriter.cs`: serialize `result.yml` per contracts/file-formats.md (task, status, attempts, started/finished, summary) and `SRC/Execution/AttemptLogWriter.cs` for claude/build/test logs with `=== attempt N/M — timestamp ===` headers
- [X] T014 [US1] Implement `SRC/Execution/TaskRunner.cs` — the orchestration state machine: ordered preconditions (config valid → environment: git + agent executable resolvable → clean tree FR-004a → task exists → pending FR-004 → specs exist → base branch exists FR-006) with no mutation on refusal; then running-commit on invoking branch → create/reuse work branch from `base:`/default → attempt loop (agent → build → test, feed failures back, attempt counter kept **in memory** — no `task.md` writes while the work branch is checked out, in-task session resume FR-012) → work commit excluding `.magnaflow/` → return to invoking branch → write final `attempts` + logs + result.yml + terminal status, one commit; map every outcome to an exit code per contracts/cli.md
- [X] T015 [US1] Wire `SRC/Commands/RunCommand.cs`: settings (task-id arg), compose ProjectConfig + TaskScanner + TaskRunner + real runners, stderr diagnostics, return TaskRunner's exit code
- [X] T016 [P] [US1] Unit tests for PromptBuilder in `TESTS/PromptBuilderTests.cs`: spec inclusion, missing-spec error, .magnaflow guard present, failure-feedback variant
- [X] T017 [P] [US1] Unit tests for ResultWriter/AttemptLogWriter in `TESTS/ResultWriterTests.cs`: yaml shape, overwrite semantics, attempt headers
- [X] T018 [US1] Unit tests for TaskRunner in `TESTS/TaskRunnerTests.cs` with fake IProcessRunner/IAgentRunner/GitClient: every precondition refusal mutates nothing (exit 2/3), happy path commit/branch call sequence matches the two-plane choreography, retry loop stops at max_attempts with `failed`, final `attempts` written only in the terminal orchestration commit (no `task.md` writes while the work branch is checked out), session resumed within task
- [X] T019 [US1] Validate quickstart.md Scenarios 1–3 manually against a disposable target project; record deviations and fix until green

**Checkpoint**: MVP — a real task can be delegated end-to-end on this machine

---

## Phase 4: User Story 2 - Pick up the next pending task (Priority: P2)

**Goal**: `mf-worker next` selects the first pending task in folder order and runs it (FR-014)

**Independent Test**: quickstart.md Scenario 4 first half (0002 done + 0003/0004 pending ⇒
next runs 0003 only; empty queue ⇒ message + exit 0)

### Implementation for User Story 2

- [X] T020 [US2] Implement `SRC/Commands/NextCommand.cs`: TaskScanner ⇒ first `pending` ⇒ delegate to TaskRunner; empty queue prints "nothing pending" and exits 0
- [X] T021 [US2] Unit tests in `TESTS/NextSelectionTests.cs`: selection order, skips non-pending and malformed tasks, empty-queue exit code 0 (selection logic with fakes)

**Checkpoint**: Task IDs no longer needed to start work

---

## Phase 5: User Story 3 - Work through all pending tasks (Priority: P3)

**Goal**: `mf-worker run-all` drains the pending queue sequentially, survives failed tasks,
then stops (FR-015/017); consecutive same-`group` tasks share one agent session (FR-013)

**Independent Test**: quickstart.md Scenario 4 second half; a failing middle task does not
stop the batch; summary lists every outcome

### Implementation for User Story 3

- [X] T022 [US3] Implement `SRC/Commands/RunAllCommand.cs`: loop scanner⇒TaskRunner until no pending remain; per-task outcome summary table at end; aggregate exit code (1 if any failed, else 0)
- [X] T023 [US3] Implement cross-task session policy in `SRC/Execution/TaskRunner.cs` + `SRC/Agents/AgentSession.cs`: carry (SessionId, Group) between consecutive tasks in one controller process; reuse when groups match and `fresh_session` is false, else new session (FR-013, data-model AgentSession)
- [X] T024 [US3] Unit tests in `TESTS/RunAllTests.cs`: batch continues past a failed task, order preserved, aggregate exit code; session reuse matrix (same group / new group / no group / fresh_session) with fake IAgentRunner

**Checkpoint**: Overnight batches work

---

## Phase 6: User Story 4 - See the state of the queue (Priority: P3)

**Goal**: `mf-worker status` prints a read-only overview (FR-016)

**Independent Test**: quickstart.md Scenario 4 status call — table matches frontmatter of
every task, `git status` unchanged afterwards

### Implementation for User Story 4

- [X] T025 [P] [US4] Implement `SRC/Commands/StatusCommand.cs`: Spectre table (ID, title, status, attempts/max) in folder order; max falls back to `defaults.max_attempts` from ProjectConfig when absent in frontmatter, shown as `-` when config itself is missing (status must work without valid config); malformed tasks shown with warning marker, zero writes/commits, exit 0
- [X] T026 [P] [US4] Unit tests in `TESTS/StatusOverviewTests.cs`: row content/order from a temp task tree, malformed row rendering, no file modification

**Checkpoint**: All four commands live

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T027 Run the full quickstart.md validation (all 5 scenarios incl. kill-mid-run restartability, SC-004) end-to-end on a fresh disposable project; fix anything red
- [X] T028 [P] Write `tools/worker-controller/README.md`: install/build, the four commands, config.yml reference (link to contracts), exit codes table
- [X] T029 [P] Add `docs/fase2-worker-controller/evaluatie-speckit.md` skeleton with findings so far (stappenplan step 7 — the research question of this phase)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 → Phase 2 → Phase 3 (US1)**: strictly sequential gates
- **US2 (Phase 4), US4 (Phase 6)**: depend only on Foundational + TaskRunner (US1)
- **US3 (Phase 5)**: depends on US1; T023 touches TaskRunner ⇒ do after US1 complete
- **Polish (Phase 7)**: after all desired stories

### Story Dependency Notes

US2/US3/US4 all layer over US1's TaskRunner — this feature's stories are deliberately NOT
mutually independent (spec: "every other command is a thin layer" over US1). US2 and US4 can
proceed in parallel once US1 is done; US3 follows either.

### Parallel Opportunities

- Phase 2: T003, T004, T005, T006 in parallel (T007 needs T006); tests T008–T010 in parallel
- Phase 3: T011, T012, T013 in parallel; T016+T017 in parallel after their targets; T014 after T011–T013
- After US1: Phase 4, Phase 6 in parallel; Phase 5 next
- Phase 7: T028, T029 in parallel

---

## Implementation Strategy

MVP first (stappenplan: "eerst `run <task-id>` end-to-end werkend, dan pas `next` en
`status`"): Phases 1–3, validate quickstart Scenarios 1–3, ideally dogfood one real task.
Then US2 → US3 → US4 as thin increments, full quickstart at the end. Commit after each task
or logical group.

## Implementation notes (2026-07-02, post-completion)

- All 29 tasks completed; 60 unit tests green (`dotnet test`).
- All 5 quickstart scenarios validated against a disposable project using a stub agent via
  `agent.command` (FR-019 substitution); real Claude Code flags (`-p`, `--output-format
  stream-json`, `--resume`) verified against the installed CLI.
- One implementation bug found by scenario validation and fixed with a regression test:
  log writers must be disposed before the terminal `git add` (FileStream buffering made
  git stage empty log files, leaving the tree dirty after a run).
- `TaskState` enum is named TaskState (not TaskStatus) to avoid clashing with
  `System.Threading.Tasks.TaskStatus`.
- Post-v0.1 amendment (same day, after dogfooding): FR-006a work-branch guard (commands
  refuse when invoked from a task's work branch; status warns) and optional `branch:`
  (branchless mode — work committed directly on the invoking branch). Spec, contracts,
  data-model, README, and EXAMPLES updated; 4 new unit tests (64 total).
- Post-v0.1 amendment (2026-07-03): FR-013a explicit session continuation. Every run
  records its final agent session ID in result.yml (`session:`); a task's `resume:`
  frontmatter continues a raw session ID verbatim or another task's recorded session
  (task-ID pattern). Wins over group continuity; resume + fresh_session is malformed.
  13 new unit tests (77 total).
