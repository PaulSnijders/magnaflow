# Implementation Plan: Worker Controller — Plan, Questions & Feedback (v0.2)

**Branch**: `002-plan-questions-feedback` | **Date**: 2026-07-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-plan-questions-feedback/spec.md`

## Summary

Extends the existing `mf-worker` CLI (`tools/worker-controller/`) with a plan-and-questions gate
and first-class feedback, and migrates its task file model onto the mf-spec prompt lane
(`docs/prompts/NNNN-{cmd|pln|qa|rst}-name.md`) in place of the retired `.magnaflow/tasks/`
folder model. Every run of a `ready` command now runs a plan phase first (a separate agent
invocation over the command + linked specs + project conventions); when nothing genuinely needs a
human it continues straight into the existing build/test execution loop in the same agent
session; when it does, the command pauses at status `questions` with the question appended to its
qa file, and the next run — after the human answers and resets status to `ready` — resumes the
same session and re-plans before continuing. Every run that reaches a terminal outcome (`done` or
`aborted`) leaves a human-readable report (rst) behind. `.magnaflow/` is reduced to machine
runtime evidence only (logs, session id) — no bookkeeping ever lives there again.

## Technical Context

**Language/Version**: C# / .NET 10 (unchanged from v0.1)

**Primary Dependencies**: Spectre.Console.Cli, CliWrap, YamlDotNet — unchanged; no new
dependency is needed (CLAUDE.md: no new dependencies without a concrete need)

**Storage**: Two-plane plain text, split by concern (spec FR-004): `docs/prompts/` holds
bookkeeping (cmd/pln/qa/rst Markdown + YAML frontmatter, git-tracked, human-and-agent-editable);
`.magnaflow/` holds only machine runtime evidence (agent/build/test logs, one small
`session.yml` per command). No database (constitution II, III).

**Testing**: xUnit; existing `IProcessRunner`/`IAgentRunner` fakes extend directly to the added
plan-phase invocation and the pause/resume gate (research R9)

**Target Platform**: Cross-platform console (unchanged)

**Project Type**: CLI tool — extends the existing `tools/worker-controller/` project (not a new
tool)

**Performance Goals**: Unchanged order of magnitude; a run now makes two agent invocations
(plan + execution) instead of one when it doesn't pause, still dominated by agent/build/test
subprocess time. `status` over 100 commands still completes in under 1 second.

**Constraints**: Still one-shot (no polling, constitution I); plan phase and execution phase MUST
share one agent session within a run (FR-010); a clean pause for questions MUST NOT consume an
`attempts` use (FR-011a); convention files are included in full, never truncated (FR-020,
matching v0.1's spec-inclusion rule).

**Scale/Scope**: Same order as v0.1 (up to 9999 commands by numbering scheme), now counted by the
`NNNN` prefix of `docs/prompts/*-cmd-*.md` files instead of `.magnaflow/tasks/` subfolders.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|-----------|-------|--------|
| I. Small, deterministic, composable | Still one-shot; the plan phase is one more deterministic step in the same run, not a scheduler; exit codes unchanged (research R8) | PASS |
| II. Plain text is the interface | cmd/pln/qa/rst are Markdown + YAML frontmatter; `.magnaflow/session.yml` is a two-line YAML file; identity lives in the filename, never duplicated inside (FR-001) | PASS |
| III. Git is the database | Status transitions still committed in place on the cmd file; two-plane commit model unchanged (bookkeeping vs. work branch); `docs/prompts/` and `.magnaflow/` are each committed on the invoking branch as before | PASS |
| IV. Observable over magical | Strictly more observable than v0.1: the pln file exposes the agent's reasoning *before* code changes, the rst file adds a synthesized narrative on top of the raw logs — no hidden state introduced | PASS |
| V. AI is a replaceable executor | Plan phase reuses the same `IAgentRunner.RunAsync` seam as execution; no JSON-schema/structured-output parsing added to the controller (research R5); conventions are just more prompt text, assembled not interpreted | PASS |
| VI. Restartable and idempotent | `questions` is a first-class, restartable checkpoint (arguably a cleaner realization of this principle than v0.1's retry loop alone); re-plan updates the pln file in place, safe to redo | PASS |
| VII. Local-first, open formats | Unchanged; the new manual-parity requirement (FR-022/FR-023) is a direct, explicit strengthening of this principle rather than a new concern | PASS |
| Technology constraints | Same C#/.NET project, same three libraries, same monorepo location | PASS |
| Testing (workflow) | Core logic (scanner, cmd/pln/qa/rst file I/O, convention loader, plan gate) unit-tested with fakes; process glue optional, as in v0.1 | PASS |

**Post-design re-check (after Phase 1)**: PASS — the data model and contracts (below) introduce no
database, no daemon behavior, no AI-specific parsing, and no artifact reachable only through the
controller (manual-parity, FR-022/FR-023 is reflected directly in the file-format contract: every
field the controller writes is plain enough for a human to write by hand). No Complexity Tracking
entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/002-plan-questions-feedback/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── cli.md            # Command surface + exit codes (amends v0.1's contracts/cli.md)
│   └── file-formats.md   # cmd/pln/qa/rst + .magnaflow/session.yml schemas
└── tasks.md              # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

This feature extends the existing `tools/worker-controller/` project in place; it does not add a
new tool. Changes relative to the tree built in `specs/001-worker-controller/plan.md`:

```text
tools/worker-controller/
├── src/MagnaFlow.WorkerController/
│   ├── Prompts/                          # renamed from Tasks/ — vocabulary now matches the spec
│   │   ├── CmdFile.cs                    # was TaskFile.cs — parses/writes NNNN-cmd-name.md
│   │   ├── CmdStatus.cs                  # was TaskState.cs — draft|ready|running|questions|done|aborted
│   │   ├── PromptScanner.cs              # was TaskScanner.cs — scans docs/prompts/ (research R2)
│   │   ├── PlnFile.cs                    # NEW — read/update NNNN-pln-name.md
│   │   ├── QaFile.cs                     # NEW — append questions / read answers in NNNN-qa-name.md
│   │   └── RstFile.cs                    # NEW — write/update NNNN-rst-name.md
│   ├── Execution/
│   │   ├── TaskRunner.cs                 # extended: plan phase + gate before the existing attempt loop (R3)
│   │   ├── PromptBuilder.cs              # extended: conventions prepended to every phase's prompt (R7)
│   │   ├── ConventionLoader.cs           # NEW — fixed-path CLAUDE.md/constitution discovery (R7)
│   │   ├── SessionEvidence.cs            # replaces ResultWriter — reads/writes .magnaflow/<id>/session.yml (R4/R6)
│   │   └── ResumeResolver.cs             # extended: default self-resume from SessionEvidence (R4)
│   ├── Agents/                           # unchanged (IAgentRunner, ClaudeCodeRunner)
│   ├── Config/                           # unchanged (ProjectConfig — .magnaflow/config.yml)
│   ├── Infrastructure/                   # unchanged (IProcessRunner, GitClient)
│   └── Commands/                         # unchanged CLI surface (run/next/run-all/status)
└── tests/MagnaFlow.WorkerController.Tests/
    ├── CmdFileTests.cs                   # was TaskFileTests.cs
    ├── PromptScannerTests.cs             # was TaskScannerTests.cs
    ├── PlnFileTests.cs                   # NEW
    ├── QaFileTests.cs                    # NEW
    ├── RstFileTests.cs                   # NEW
    ├── ConventionLoaderTests.cs          # NEW
    ├── SessionEvidenceTests.cs           # was ResultWriterTests.cs
    ├── ResumeResolverTests.cs            # extended
    └── TaskRunnerTests.cs                # extended: plan-gate, pause, and resume scenarios
```

**Structure Decision**: Same single-console-project-plus-one-test-project shape as v0.1 (KISS; no
new class library). The `Tasks/` → `Prompts/` rename and `TaskFile`/`TaskState` →
`CmdFile`/`CmdStatus` renames (research R1) keep the code's vocabulary aligned with the spec's;
this is an internal, pre-1.0 tool with a single consumer (this monorepo), so the rename carries no
compatibility cost. No new project-level seams are needed beyond the two new small classes
(`ConventionLoader`, `SessionEvidence`) — `IAgentRunner`/`IProcessRunner` already provide the only
process-boundary seams required (constitution V/VI).

## Complexity Tracking

No constitution violations — table not needed.
