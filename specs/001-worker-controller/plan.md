# Implementation Plan: MagnaFlow Worker Controller (v0.1)

**Branch**: `001-worker-controller` | **Date**: 2026-07-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-worker-controller/spec.md`

## Summary

A one-shot .NET console tool (`mf-worker`) that executes AI-delegated tasks in a target
project: it reads a task folder under `.magnaflow/tasks/`, marks it running, creates the task's
work branch, prompts Claude Code headless with the task + linked specs, runs the project's
build/test commands with bounded retry feedback, and writes logs, `result.yml`, and the final
status back as committed plain-text files. Four subcommands: `run <task-id>`, `next`,
`run-all`, `status`.

## Technical Context

**Language/Version**: C# / .NET 10 (LTS)

**Primary Dependencies**: Spectre.Console.Cli (command parsing + console output), CliWrap
(external process execution with output streaming), YamlDotNet (frontmatter, `config.yml`,
`result.yml`)

**Storage**: Plain-text files in the target project (`.magnaflow/` folder); git as history.
No database (constitution II, III).

**Testing**: xUnit; core logic testable without processes via an `IProcessRunner` abstraction
with fakes (constitution: core-logic tests mandatory)

**Target Platform**: Cross-platform console (Windows first; Linux/macOS supported by .NET)

**Project Type**: CLI tool in monorepo subfolder `tools/worker-controller/`

**Performance Goals**: Negligible controller overhead; wall time is dominated by agent, build,
and test subprocesses. `status` over 100 tasks completes in under 1 second.

**Constraints**: One-shot execution (no polling); no AI logic in the tool; all state as
committed plain text; restartable/idempotent; external command timeout configurable
(default 30 minutes per command)

**Scale/Scope**: Up to 9999 tasks per target project; single developer, single controller
instance, no locking (spec assumption)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|-----------|-------|--------|
| I. Small, deterministic, composable | One-shot CLI, four subcommands, no polling (FR-017); exit codes for composition (FR-021) | PASS |
| II. Plain text is the interface | All I/O is Markdown/YAML/log files; folder name = task ID | PASS |
| III. Git is the database | Status transitions committed in place; work on task branches; files never move | PASS |
| IV. Observable over magical | claude.log / build.log / test.log / result.yml per task; no hidden state | PASS |
| V. AI is a replaceable executor | Agent = configurable external command; controller only builds prompts and relays text (FR-019) | PASS |
| VI. Restartable and idempotent | Pending-only precondition (FR-004), dirty-tree refusal (FR-004a), branch reuse, stuck-`running` refusal | PASS |
| VII. Local-first, open formats | Runs entirely on the dev machine; Markdown/YAML/plain logs only | PASS |
| Technology constraints | C#/.NET console app in `tools/worker-controller/`; Spectre.Console.Cli, CliWrap, YamlDotNet — all sanctioned | PASS |
| Testing (workflow) | Core logic (scanner, frontmatter, config, prompt builder, run loop) unit-tested with fakes; process glue optional | PASS |

**Post-design re-check (after Phase 1)**: PASS — design introduces no databases, no daemon
behavior, no AI-specific logic; the two-plane commit model (research.md R6) strengthens
principle III rather than violating it. No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/001-worker-controller/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── cli.md           # Command surface + exit codes
│   └── file-formats.md  # task.md / config.yml / result.yml schemas
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
tools/worker-controller/
├── WorkerController.sln
├── src/
│   └── MagnaFlow.WorkerController/
│       ├── Program.cs                  # Spectre.Console.Cli app wiring
│       ├── Commands/                   # RunCommand, NextCommand, RunAllCommand, StatusCommand
│       ├── Tasks/                      # TaskScanner, TaskFile (frontmatter parse/update), TaskStatus
│       ├── Config/                     # ProjectConfig loader (.magnaflow/config.yml)
│       ├── Execution/                  # TaskRunner (the loop), PromptBuilder, ResultWriter
│       ├── Agents/                     # IAgentRunner + ClaudeCodeRunner (claude -p, sessions)
│       └── Infrastructure/             # IProcessRunner (CliWrap impl), GitClient, Clock
└── tests/
    └── MagnaFlow.WorkerController.Tests/
        ├── TaskScannerTests.cs
        ├── TaskFileTests.cs            # frontmatter parse + in-place update
        ├── ProjectConfigTests.cs
        ├── PromptBuilderTests.cs
        ├── TaskRunnerTests.cs          # retry loop state machine with fakes
        └── ResultWriterTests.cs
```

**Structure Decision**: Monorepo tool subfolder per constitution. Single console project plus
one test project — no extra class libraries (KISS); internal namespaces provide the layering.
`IProcessRunner` and `IAgentRunner` are the only seams needed to keep core logic testable
without real subprocesses.

## Complexity Tracking

No constitution violations — table not needed.
