# Technical

The design system of a tool repo: the principles every MagnaFlow tool
follows, the shared technology, and how the repo itself is worked on.
Page specs reference this file instead of restating a principle; they
record only deviations. Formerly the Spec Kit constitution
(`.specify/memory/constitution.md`, v1.1.0, ratified 2026-07-02),
ported here on adoption of the own spec system.

## Principles

### I. Small, deterministic, composable tools

Every tool is a small, single-purpose program. One-shot tools
(mf-worker, mf-run) do their job and stop; the only long-running loops
are explicit orchestrators (mf-watch polls and dispatches, mf-cockpit
serves a window on the files). A tool never decides "what next" for
another tool's domain — it composes others by spawning them, never by
library reference. Same inputs, same actions: all variability comes
from the external processes a tool orchestrates.

### II. Plain text is the interface

Everything that matters is a plain-text file: Markdown for humans, YAML
for machines, YAML frontmatter where both meet (`NNNN-cmd-*.md`). No
databases, no binary formats, no hidden state. Identity lives in the
filename (the lane's `NNNN-` prefix), never duplicated in content.

### III. Git is the database

The repository is the source of truth and the history. Every state
transition of a command (status in the cmd frontmatter, the rst report,
qa answers) is committed; files never move between folders, status
changes in place. Bookkeeping commits carry the tool's own prefix
(`mf-worker: …`, `cockpit: …`), see
[command lifecycle](command-lifecycle.md). Code work happens on the
invoking branch by default; a cmd opts into a work branch with
`branch:`, which the worker pushes and a human merges.

### IV. Observable over magical

Every step a tool takes logs to files: raw agent, build and test output
stream to `.magnaflow/<id>/claude.log`, `build.log`, `test.log`; mf-watch
and the cockpit keep their own diaries. Those raw logs are machine-local
(gitignored, megabytes of unreviewable output); the committed audit
trail is the rst report and its `summary:` line. A user must be able to
reconstruct what happened — and who is at bat — from the files alone.
When in doubt, log more.

### V. AI is a replaceable executor

Tools contain no AI logic; they orchestrate external processes (Claude
Code, git, build, test) behind `IProcessRunner`/`IAgentRunner`. Prompts
are built from the cmd file, linked specs and the target repo's own
conventions — knowledge lives in the repo, not in code. Swapping the
agent touches only the invocation layer and configuration
(`agent.command` / `agent.args`).

### VI. Restartable and idempotent

Every tool is safe to re-run: an interrupted run leaves files from which
the tool or a human can resume or retry. Retries are bounded
(`max_attempts`) and counted; `aborted` is a first-class terminal status,
not an exception. Tools verify preconditions (clean tree, expected
status) before acting and refuse rather than guess.

### VII. Local-first, open formats

Everything works on the developer's own machine with no cloud service in
the loop: only the repository and locally installed tools. All formats
are open and vendor-neutral, readable without MagnaFlow.

## Shared conventions

- **Exit codes** (CLI tools): 0 success, 1 the work itself failed, 2
  usage error, 3 precondition refused, 4 environment error. A tool that
  needs fewer codes uses a subset with the same meaning; each tool's
  spec lists its own table (mf-run counts an invalid config as 2).
- **`--help` / `-h`** on every executable prints usage and exits 0; an
  unknown argument names itself on stderr, prints usage and exits 2.
- **Paths in messages and config** are repo-relative where they refer
  to a project, absolute where they refer to the machine.
- **Cockpit appearance**: dark theme derived from
  `assets/magnaflow-banner-wide.png`, plain HTML + CSS in `wwwroot/`, no
  SPA framework; the logo and banner are build-time links from
  `assets/`.

## Technology

- C#/.NET 10; each tool a standalone cross-platform console app in
  `tools/<name>/` with its own solution, `src/` and `tests/`.
- Libraries: Spectre.Console.Cli, CliWrap, YamlDotNet. New dependencies
  need a concrete need — KISS.
- AI execution: Claude Code headless (`claude -p`), session reuse via
  `--resume`; the user's own account, API keys only as a deliberate
  opt-in.

## Working on this repo

- Spec-first with the own spec system: `docs/specs/README.md`. The one
  rule — any behavioral change updates its spec in the same commit.
- Core logic of every tool is covered by xUnit tests; glue code (CLI
  wiring, process invocation) tests are encouraged, not required.
- Build and tests run in every worker run (`.magnaflow/config.yml`);
  failures feed bounded retries (principle VI).
- Narrowest end-to-end path first, then widen.
- Changing a principle here is a decision: write it in
  `docs/decisions/` and cite it below.

## Quality baseline

Advise only (decision 0017); recorded 2026-10-06 on the spec-kit 1.2
update.

- **On today**: nothing beyond the SDK defaults — no
  `TreatWarningsAsErrors`, no `AnalysisLevel`, no `Directory.Build.props`.
  All four tool solutions build with 0 warnings at those defaults.
- **Advised, as a separate cmd**: a `Directory.Build.props` at the repo
  root with `TreatWarningsAsErrors` and `AnalysisLevel`. At
  `latest-recommended` the build reports (unique warnings) mf-cockpit
  321, worker-controller 253, mf-watch 29, mf-run 3 — mostly CA1707
  (underscores in xUnit test names), then CA1051, CA1310, CA1305,
  CA1816. So the cmd decides per rule (e.g. CA1707 off for test
  projects) before switching errors on.
- **Vulnerability scan**: none — there is no CI here.
  `dotnet list package --vulnerable` by hand is the stand-in.

Code: tools/
Why: decisions/0001-worker-workflow-v0-1.md
