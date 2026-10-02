# Research: MagnaFlow Worker Controller (v0.1)

All Technical Context entries were pinned by prior project decisions
(`docs/fase2-worker-controller/speckit-stappenplan.md`, workflow-v0.1.md, constitution); no
NEEDS CLARIFICATION markers remained. This document records the concrete design decisions and
their rationale.

## R1 — Runtime: .NET 10 (LTS)

- **Decision**: Target `net10.0`, C# latest, single console project.
- **Rationale**: Current LTS (support to Nov 2028); cross-platform; matches team expertise.
- **Alternatives considered**: .NET 8 LTS (support ends Nov 2026 — too short for a foundation
  tool); Go/Python (rejected earlier in fase 1, see docs).

## R2 — CLI framework: Spectre.Console.Cli

- **Decision**: `CommandApp` with four commands: `run <task-id>`, `next`, `run-all`, `status`.
  Settings classes per command; `--project <path>` global option defaulting to the current
  working directory (the target project root).
- **Rationale**: Declarative command/settings model, good help output, and Spectre.Console
  table rendering for `status`. Already sanctioned by the constitution.
- **Alternatives considered**: System.CommandLine (fine, but Spectre chosen in fase 1 and gives
  console rendering for free); manual arg parsing (needless).

## R3 — Process execution: CliWrap

- **Decision**: All external commands (agent, git, build, test) run through one
  `IProcessRunner` abstraction implemented with CliWrap. Stdout/stderr are streamed line-wise
  to the task-folder log files as they arrive (FR-008) and mirrored to the console. Every
  command gets a `CancellationToken` from a configurable timeout (default 30 min/command).
- **Rationale**: CliWrap gives piping/streaming/exit-code handling without `Process` footguns;
  a single seam keeps the run loop unit-testable with fakes.
- **Alternatives considered**: Raw `System.Diagnostics.Process` (more code, easy deadlocks on
  full output buffers).

## R4 — YAML + frontmatter handling: YamlDotNet + surgical updates

- **Decision**: Parse frontmatter by splitting `task.md` on the `---` fence pair, feeding the
  YAML block to YamlDotNet for reading. For writes, the controller updates only the `status:`
  and `attempts:` lines in place (line-targeted replacement inside the frontmatter block); it
  never re-serializes the whole document. `config.yml` is read with YamlDotNet;
  `result.yml` is written fresh by serialization each run (controller-owned file).
- **Rationale**: Re-serializing frontmatter would destroy user comments and formatting and
  produce noisy diffs — the opposite of constitution III's "git-diff shows the lifecycle".
  Line-targeted edits give minimal, human-readable diffs (`status: pending` → `status:
  running`).
- **Alternatives considered**: Markdig + YAML pipeline (heavier, same problem); full
  YamlDotNet round-trip (loses comments/ordering).

## R5 — Claude Code headless invocation

- **Decision**: `ClaudeCodeRunner : IAgentRunner` invokes
  `claude -p <prompt> --output-format stream-json --verbose` in the target project root,
  writing every emitted JSON line verbatim to `claude.log` (raw output = the audit trail).
  The session ID is taken from the stream's init event and stored in memory for the duration
  of the controller run. Follow-up prompts within a task, and tasks continuing the same
  `group:`, add `--resume <session-id>`. Extra agent arguments come verbatim from
  `config.yml` (`agent.args`), which is where a project opts into
  `--dangerously-skip-permissions` (FR-008a); the default is no extra flags, i.e. the agent's
  own safe behavior. The agent executable itself is configurable (`agent.command`, default
  `claude`) per FR-019.
- **Rationale**: `stream-json` provides both live streaming (FR-008) and machine-readable
  session IDs for the `--resume` policy (FR-012/013) — plain text output provides neither.
  Verbatim pass-through of `agent.args` keeps all agent-specific policy in project config,
  not in code (constitution V).
- **Alternatives considered**: `--output-format json` (no live streaming); interactive PTY
  automation (explicitly rejected in fase 1); Claude Agent SDK (adds AI-coupled code paths;
  CLI keeps the executor swappable).
- **Note for implementation**: verify exact flag names against the installed Claude Code
  version at build time; the contract here is "headless, streaming, resumable session,
  pass-through args", not specific flag spellings.

## R6 — Git: CLI via GitClient + two-plane commit model

- **Decision**: All git operations shell out to `git` through `IProcessRunner` (a thin
  `GitClient`: status --porcelain, branch existence, checkout -b/checkout, add, commit).
  Commits are split over two planes:
  - **Orchestration plane** — the branch the controller was started on. Carries the
    `.magnaflow/` changes: `status: running` commit before work starts; logs, `result.yml`,
    and the terminal `status: done|failed` commit after the run. Only paths under
    `.magnaflow/` are staged here.
  - **Work plane** — the task's work branch (created from `base:` or the default branch,
    FR-006). Carries the agent's code changes; the controller commits everything except
    `.magnaflow/` there before switching back.
  Sequence per task: verify clean tree → commit `running` on the invoking branch → checkout
  work branch → agent + build/test attempts → commit work on work branch → checkout invoking
  branch → write logs/result/status → commit orchestration files.
  **Invariant**: `task.md` is only ever written while the invoking branch is checked out
  (the `running` commit before checkout; final `attempts` + terminal status after checkout
  back). During the attempt loop the counter lives in memory — the work branch may have a
  different or absent copy of the task folder (older `base:`), so writing it there would
  target the wrong file and can make the checkout back fail.
- **Rationale**: This resolves the spec's deferred choreography question with the only layout
  where `run-all`/`next` work: the queue scanner reads the invoking branch, so terminal
  statuses must land there — not on unmerged work branches (a `done` visible only on an
  unmerged branch would make `run-all` re-execute finished tasks). It also keeps work-branch
  diffs pure code for review. Log files written during execution are untracked until the final
  orchestration commit, so they survive branch switches without polluting the work branch.
- **Alternatives considered**: everything on the work branch (breaks queue scanning until
  merge — rejected as unsound); LibGit2Sharp (native binary dependency; constitution V says
  orchestrate existing tools — git is one of them).
- **Consequence for prompts**: the agent is instructed never to touch `.magnaflow/` and never
  to run git commit itself; the controller owns all commits.

## R7 — Exit codes (FR-021)

- **Decision**: `0` = requested work succeeded (task done / batch fully done / status shown /
  nothing pending), `1` = task(s) ended `failed`, `2` = usage or configuration error (unknown
  task, malformed config, missing base branch), `3` = precondition refusal (dirty tree, task
  not pending), `4` = environment error (git or agent executable unavailable).
- **Rationale**: A small stable vocabulary the future dispatcher can branch on without parsing
  text. "Nothing pending" is 0, not an error — an empty queue is a normal outcome for a
  one-shot tool.

## R8 — Testing strategy

- **Decision**: xUnit. Core logic — `TaskScanner`, `TaskFile` (parse + surgical update),
  `ProjectConfig`, `PromptBuilder`, `TaskRunner` retry/session state machine, `ResultWriter` —
  is tested against temp directories and fake `IProcessRunner`/`IAgentRunner` implementations.
  Process glue (`CliWrapProcessRunner`, real `GitClient`) is covered by the quickstart's
  end-to-end validation rather than unit tests (constitution: core logic mandatory, glue
  optional).
- **Rationale**: The retry loop and frontmatter surgery are where correctness lives and where
  regressions would corrupt user repositories; they must be provable without spawning
  processes.

## R9 — config.yml shape

- **Decision**: Minimal schema (see contracts/file-formats.md): `build.command`,
  `test.command`, `defaults.max_attempts`, `defaults.command_timeout_minutes`,
  `agent.command`, `agent.args`. Commands are executed via the platform shell so users can
  write natural command lines (`dotnet build`, `npm test`).
- **Rationale**: Smallest contract that satisfies FR-009/FR-008a/FR-019 and the timeout
  assumption; everything else defaults sensibly.
