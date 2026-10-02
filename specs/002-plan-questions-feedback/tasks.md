---

description: "Task list for Worker Controller — Plan, Questions & Feedback (v0.2)"
---

# Tasks: Worker Controller — Plan, Questions & Feedback (v0.2)

**Input**: Design documents from `/specs/002-plan-questions-feedback/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Core-logic tests are MANDATORY per the constitution (file I/O for cmd/pln/qa/rst,
scanner, convention loader, the plan/pause/resume gate). Process glue (real `IAgentRunner`,
`GitClient`) is validated via quickstart scenarios instead, as in v0.1.

**Organization**: Tasks grouped by user story from spec.md. All four stories are P1/P2, but US1
("plan gates execution") is the load-bearing increment the other three build on, same as v0.1's
"every other command is a thin layer" relationship.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 = plan + self-answer, US2 = pause/resume, US3 = report, US4 = conventions

## Path Conventions

Everything lives in the existing `tools/worker-controller/`; `SRC` =
`tools/worker-controller/src/MagnaFlow.WorkerController`, `TESTS` =
`tools/worker-controller/tests/MagnaFlow.WorkerController.Tests`.

---

## Phase 1: Setup

**Purpose**: Rename the v0.1 vocabulary to match the spec, with no behavior change yet

- [X] T001 Rename `SRC/Tasks/` → `SRC/Prompts/` (namespace `MagnaFlow.WorkerController.Tasks` →
  `MagnaFlow.WorkerController.Prompts`); rename `TaskState.cs`→`CmdStatus.cs`,
  `TaskFile.cs`→`CmdFile.cs`, `TaskScanner.cs`→`PromptScanner.cs`; update every `using`/reference
  site across `SRC/` and `TESTS/` (research R1). Rename the corresponding test files
  (`TaskFileTests.cs`→`CmdFileTests.cs`, `TaskScannerTests.cs`→`PromptScannerTests.cs`). No
  behavior change in this task — verify `dotnet build` and `dotnet test` still pass with the old
  4-value status semantics before moving on.

**Checkpoint**: Solution still builds and all existing tests pass under the new names

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The new file model (`docs/prompts/` cmd/pln/qa/rst + `.magnaflow/` runtime evidence)
that every user story depends on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 Extend `SRC/Prompts/CmdStatus.cs` to the 6-value set `draft | ready | running |
  questions | done | aborted` (parser + serializer); update every consumer of the old 4-value
  enum (`pending`→`ready`, `failed`→`aborted`) in `Commands/` and `Execution/` (data-model.md
  CmdStatus)
- [X] T003 Update `SRC/Prompts/CmdFile.cs`: identity/`FilePath` now resolve against the flat
  `docs/prompts/` location (no per-command subfolder for the cmd file itself); add an
  `EvidenceDirectory(projectRoot)` helper returning `.magnaflow/<NNNN-name>/` (data-model.md
  Command; depends on T002 for the new status parser)
- [X] T004 Update `SRC/Prompts/PromptScanner.cs`: scan `docs/prompts/` (not
  `.magnaflow/tasks/`) for `^\d{4}-cmd-[a-z0-9-]+\.md$`; derive the `NNNN-name` identity; add
  sibling-path helpers for the pln/qa/rst infix substitution; report a pln/qa/rst file with no
  matching cmd file as malformed/orphaned rather than crashing the scan (research R2, FR-001,
  edge case; depends on T003)
- [X] T005 [P] Implement `SRC/Prompts/PlnFile.cs`: read/write `NNNN-pln-name.md` (Plan /
  Self-answered questions / Open questions sections per contracts/file-formats.md);
  `ReadOpenQuestions()` extracts the "Open questions (this round)" section as a list of strings
- [X] T006 [P] Implement `SRC/Prompts/QaFile.cs`: `AppendQuestions(IEnumerable<string>)`
  (round-numbered headers + a blank `**Answer**:` line, appending to an existing file or creating
  a new one); `HasUnansweredQuestion()`; `ReadLatestAnswers()` for the resume path
  (contracts/file-formats.md)
- [X] T007 [P] Implement `SRC/Prompts/RstFile.cs`: existence/timestamp check for
  `NNNN-rst-name.md` (What was done / Decisions and deviations / Abort reason) — executor-owned,
  so the controller only verifies presence, never authors content itself (FR-017)
- [X] T008 [P] Implement `SRC/Execution/SessionEvidence.cs`: read/write
  `.magnaflow/<NNNN-name>/session.yml` (`session:` field only); delete `SRC/Execution/ResultWriter.cs`
  and its test file, superseded (research R4/R6)
- [X] T009 [P] Implement `SRC/Execution/ConventionLoader.cs`: fixed-order lookup —
  `CLAUDE.md` (repo root), else `.specify/memory/constitution.md`, else
  `docs/constitution.md`; returns concatenated content or empty string; missing files silently
  skipped (FR-020/FR-021, research R7)
- [X] T010 Update `SRC/Execution/ResumeResolver.cs`: resolve `resume:` against the `docs/prompts/`
  cmd identity and `SessionEvidence` instead of `.magnaflow/tasks/` + `result.yml`; precedence
  rules unchanged otherwise (v0.1 FR-013a; depends on T004, T008)
- [X] T011 [P] Rewrite `TESTS/CmdFileTests.cs` (was TaskFileTests.cs) for the new location and
  6-value status set
- [X] T012 [P] Rewrite `TESTS/PromptScannerTests.cs` (was TaskScannerTests.cs): scans
  `docs/prompts/`, sibling-path helpers, orphaned-sibling detection
- [X] T013 [P] Unit tests `TESTS/PlnFileTests.cs`, `TESTS/QaFileTests.cs`, `TESTS/RstFileTests.cs`:
  round-trip read/write; `ReadOpenQuestions`/`HasUnansweredQuestion`/`ReadLatestAnswers` parsing;
  multi-round qa append
- [X] T014 [P] Unit tests `TESTS/SessionEvidenceTests.cs` (was ResultWriterTests.cs) and
  `TESTS/ConventionLoaderTests.cs`: fixed discovery order, absent-file tolerance
- [X] T015 Update `TESTS/ResumeResolverTests.cs` for the new resolution target (depends on T010)

**Checkpoint**: The full cmd/pln/qa/rst + session-evidence file model is readable, writable, and
provable — user story implementation can begin

---

## Phase 3: User Story 1 - Plan and self-answer before touching code (Priority: P1) 🎯 MVP

**Goal**: Every run of a `ready` command executes a plan phase first; self-answered questions are
recorded in the pln file; when nothing is genuinely open, the controller continues immediately,
same session, into the existing build/test loop (FR-008–FR-011a, research R3)

**Independent Test**: quickstart.md Scenario A — a fully-specified command produces a pln file and
completes in one run without pausing

### Implementation for User Story 1

- [X] T016 [P] [US1] Extend `SRC/Execution/PromptBuilder.cs`: `BuildPlan(cmd, projectRoot,
  conventions)` — the plan-phase prompt (conventions + cmd body + linked specs; instructs the
  agent to write its plan, self-answered Q&A, and any open questions into its own
  `NNNN-pln-name.md`, per contracts/file-formats.md's pln section) (FR-008/FR-009)
- [X] T017 [US1] Extend `SRC/Execution/PromptBuilder.cs`'s existing initial/failure-feedback
  prompt builders to prepend `ConventionLoader` output ahead of the cmd body (FR-008 applies
  conventions to execution too, not just planning)
- [X] T018 [US1] Extend `SRC/Execution/TaskRunner.cs`: insert the plan-phase invocation before the
  existing attempt loop — invoke the agent with the plan prompt, then read
  `PlnFile.ReadOpenQuestions()`: empty ⇒ continue immediately into the existing build/test loop
  in the same run and session (FR-010); non-empty ⇒ append the questions to the qa file via
  `QaFile.AppendQuestions`, set `status: questions`, commit, and end the run **without**
  incrementing `attempts` and without entering the build/test loop (FR-011, FR-011a)
- [X] T019 [US1] Update the precondition check in `SRC/Execution/TaskRunner.cs`: only `status:
  ready` is runnable; every other status (`draft`, `running`, `questions`, `done`, `aborted`)
  refuses with an explanatory message and no mutation (amends v0.1 FR-004 per spec FR-006)
- [X] T020 [US1] Update `SRC/Commands/RunCommand.cs`, `NextCommand.cs`, `RunAllCommand.cs`,
  `StatusCommand.cs`/`StatusOverview.cs` to the renamed `PromptScanner`/`CmdFile`/`CmdStatus`
  types and `ready`/`aborted` terminology
- [X] T021 [P] [US1] Unit tests in `TESTS/TaskRunnerTests.cs`: plan phase invoked before any
  build/test command; pln file exists before build/test runs; the no-open-question path
  continues into execution within the same run and session
- [X] T022 [US1] Validate quickstart.md Scenario A manually against a disposable target project;
  fix deviations until green

**Checkpoint**: A well-specified command plans, self-answers, and completes end-to-end without
ever pausing — the MVP slice

---

## Phase 4: User Story 2 - Pause on genuine questions, resume after the human answers (Priority: P1)

**Goal**: A pause writes to the qa file and ends the run without a build/test attempt; after the
human answers and resets status to `ready`, the next run resumes the same session by default and
re-plans in place before continuing (FR-011–FR-015, research R4)

**Independent Test**: quickstart.md Scenario B — pause, edit the qa file, reset status, rerun;
verify the same session resumes and the command reaches a terminal status

### Implementation for User Story 2

- [X] T023 [US2] Extend `SRC/Execution/TaskRunner.cs`: before choosing a session for the
  plan-phase invocation, consult `SessionEvidence` for this command; when present and no
  explicit `resume:`/`group` continuity applies, use it as the default resumed session (FR-013,
  amends v0.1 FR-013a precedence — explicit `resume:`/`group` still win)
- [X] T024 [US2] Extend `SRC/Execution/TaskRunner.cs`: write/update `SessionEvidence` at the end
  of **every** run — paused (`questions`) or terminal — with the session ID the run ended on
  (FR-013 depends on evidence surviving a pause, not just a terminal outcome)
- [X] T025 [US2] Verify/extend `SRC/Prompts/PlnFile.cs`'s write path so a re-plan on a resumed run
  updates the **same** pln file in place (overwrite, not a new file or an appended history)
  (FR-014)
- [X] T026 [US2] Verify/extend `SRC/Prompts/QaFile.cs` so a second (or later) pause round appends
  a new question/answer entry to the **same** qa file rather than creating a new one (FR-015)
- [X] T027 [US2] Handle "recorded session no longer resumable" in `SRC/Execution/TaskRunner.cs`:
  when the agent rejects a resume, report a clear environment/usage error before any mutation
  rather than silently starting a fresh session (spec Edge Cases)
- [X] T028 [P] [US2] Unit tests in `TESTS/TaskRunnerTests.cs`: a resumed run picks up
  `SessionEvidence`'s session by default; explicit `resume:`/`group` still take precedence; a
  re-plan overwrites the same pln file; a second pause round appends to the same qa file; a
  command still in `questions` is refused when run directly (acceptance-scenario regression using
  the precondition check from T019)
- [X] T029 [US2] Validate quickstart.md Scenario B manually (full pause → answer → resume cycle)
  against a disposable target project

**Checkpoint**: A paused command can be unblocked by a plain hand-edit and picks its context back
up with zero repeated context

---

## Phase 5: User Story 3 - A report the next design conversation can read (Priority: P1)

**Goal**: The rst file is written/updated only when a run's execution phase reaches `done` or
`aborted`, reflecting the final state, deviations from the plan, and (for `aborted`) the specific
reason; never for a run ending `questions` (FR-017–FR-019)

**Independent Test**: quickstart.md Scenario C — an aborted run's rst states the specific reason
and reflects the final attempt

### Implementation for User Story 3

- [X] T030 [US3] Extend `SRC/Execution/PromptBuilder.cs`'s execution-phase prompts (initial +
  failure-feedback): instruct the agent to write/update its own `NNNN-rst-name.md` (What was
  done / Decisions and deviations / Abort reason) once the run concludes, matching the terminal
  outcome it's heading toward (FR-017; executor-owned, same ownership model as pln)
- [X] T031 [US3] Extend `SRC/Execution/TaskRunner.cs`: on a terminal outcome (`done`/`aborted`),
  verify the rst file exists (log a warning, don't fail the run, if the agent didn't produce one —
  authoring it is the agent's job per FR-017); thread the specific `aborted` reason (retries
  exhausted / unrecoverable environment error) into the final prompt so the agent captures it in
  the rst
- [X] T032 [US3] Add an explicit guard in `SRC/Execution/TaskRunner.cs` confirming a run ending at
  `questions` never triggers rst-writing instructions (FR-019) — defensive, since the pause branch
  (T018) already returns before the execution phase that would produce one
- [X] T033 [P] [US3] Unit tests in `TESTS/TaskRunnerTests.cs`: `done`/`aborted` outcomes trigger
  the rst-producing prompt instruction with the right reason threaded through; a `questions`-ending
  run does not
- [X] T034 [US3] Validate quickstart.md Scenario C manually (aborted outcome with a stated reason)
  against a disposable target project

**Checkpoint**: Every terminal run leaves a report a design conversation can read without the diff

---

## Phase 6: User Story 4 - The worker follows the project's own conventions (Priority: P2)

**Goal**: Confirm `ConventionLoader` (built in Foundational, T009) is actually wired into both
phases' prompts end-to-end, and behaves correctly across all-present/one-present/none-present
combinations (FR-020/FR-021)

**Independent Test**: quickstart.md Scenario D

### Implementation for User Story 4

- [X] T035 [US4] Confirm `SRC/Execution/PromptBuilder.cs` (T016/T017) prepends
  `ConventionLoader.Load(projectRoot)`'s output to both the plan-phase and execution-phase
  prompts, ahead of the cmd body; close any gap found
- [X] T036 [P] [US4] Integration-level unit test in `TESTS/PromptBuilderTests.cs`: a fully built
  prompt (plan or execution) contains `CLAUDE.md`/constitution content when present, and omits it
  cleanly (no error, no placeholder) when absent, across the exact fixed discovery order
- [X] T037 [US4] Validate quickstart.md Scenario D manually (both files present, then neither)
  against a disposable target project

**Checkpoint**: House conventions reach the agent automatically, with or without being listed in
`specs:`

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T038 Migrate `tools/worker-controller/examples/hello-website/` from `.magnaflow/tasks/` to
  `docs/prompts/` (cmd/pln/qa/rst) per contracts/file-formats.md; update `EXAMPLES.md`
- [X] T039 [P] Update `tools/worker-controller/README.md`: new file locations, the 6-value status
  vocabulary, the plan/questions/resume/report cycle, convention inheritance, exit code table
  (contracts/cli.md v0.2)
- [X] T040 Run the full quickstart.md validation (all 5 scenarios, including Scenario E's
  manual-parity walkthrough) end-to-end on a fresh disposable project; fix anything red
- [X] T041 [P] Add a v0.2 completion note (deviations found, bugs fixed) alongside v0.1's, in
  `docs/fase2-worker-controller/`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1) → Foundational (Phase 2)**: strictly sequential; Foundational needs the
  renamed files in place first
- **User Stories (Phase 3+)**: all depend on Foundational completion
  - US1 (Phase 3) has no dependency on US2–US4
  - US2 (Phase 4) depends on US1 (extends the same `TaskRunner` gate T018 introduced)
  - US3 (Phase 5) depends on US1 (extends the same `TaskRunner`/`PromptBuilder`); independent of
    US2
  - US4 (Phase 6) depends on Foundational's `ConventionLoader` (T009) and US1's PromptBuilder
    wiring (T016/T017); independent of US2/US3 otherwise
- **Polish (Phase 7)**: after all desired stories

### Story Dependency Notes

As in v0.1, these stories are deliberately NOT mutually independent — US2/US3/US4 all extend the
same `TaskRunner`/`PromptBuilder` that US1 introduces (spec: "everything else builds on this gate
existing"). US3 and US4 can proceed in parallel once US1 is done; US2 should land before or
alongside them since it touches the same pause branch US3's abort-reason threading also touches.

### Parallel Opportunities

- Foundational: T005, T006, T007, T008, T009 in parallel (independent new files); T011–T014 in
  parallel once their respective targets exist
- US1: T016 in parallel with early US1 test scaffolding; T021 after T018 lands
- US2: T028 after T023–T027 land
- US3: T033 after T030–T032 land
- US4: T036 in parallel with T035/T037
- Phase 7: T039 and T041 in parallel; T038 and T040 are sequential (validation needs the migrated
  example)

---

## Parallel Example: Foundational Phase

```bash
# Launch the independent new file types together:
Task: "Implement SRC/Prompts/PlnFile.cs"
Task: "Implement SRC/Prompts/QaFile.cs"
Task: "Implement SRC/Prompts/RstFile.cs"
Task: "Implement SRC/Execution/SessionEvidence.cs"
Task: "Implement SRC/Execution/ConventionLoader.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (rename)
2. Complete Phase 2: Foundational (file model) — CRITICAL, blocks all stories
3. Complete Phase 3: User Story 1 (plan gate, continue-without-pausing path)
4. **STOP and VALIDATE**: quickstart Scenario A on a disposable project
5. This alone already changes v0.1's behavior meaningfully (every run now plans first) — a
   reasonable checkpoint even though the pause/resume/report value isn't visible yet

### Incremental Delivery

1. Setup + Foundational → file model ready
2. US1 → plan gate works for the happy path → validate Scenario A
3. US2 → pause/resume cycle works → validate Scenario B
4. US3 → reports land → validate Scenario C
5. US4 → conventions confirmed wired → validate Scenario D
6. Polish → migrate the example project, docs, full quickstart including Scenario E

## Notes

- [P] tasks = different files, no dependency on an incomplete task
- [Story] label maps task to specific user story for traceability
- Commit after each task or logical group
- Stop at any checkpoint to validate independently
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independence beyond
  the shared-`TaskRunner` relationship already called out above
