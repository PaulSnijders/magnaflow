---
title: Support multiple build/test commands in config.yml
status: draft
branch: task/0001-multi-build-commands
base: main
specs:
  - specs/001-worker-controller/contracts/file-formats.md
attempts: 0
max_attempts: 3
created: 2026-07-13
---

## Goal

The Worker Controller (`tools/worker-controller/`) implements the updated
`.magnaflow/config.yml` contract: the `build` and `test` sections each accept
either `command:` (a single string) or `commands:` (a list of strings, run
sequentially). The contract in
`specs/001-worker-controller/contracts/file-formats.md` (section
"`.magnaflow/config.yml`") and `tools/worker-controller/README.md` already
describe this; the code does not implement it yet.

## Context

The gap is exactly two source files plus their tests:

- `src/MagnaFlow.WorkerController/Config/ProjectConfig.cs` — `CommandDto` only
  has a `Command` string; validation demands `build.command` / `test.command`
  and rejects a config that uses `commands:`.
- `src/MagnaFlow.WorkerController/Execution/TaskRunner.cs` — the build and test
  phases each run a single `config.BuildCommand` / `config.TestCommand` via
  `processes.RunShellAsync(...)`.

Contract semantics to implement (the contract wins on any conflict):

- Exactly one of `command:` / `commands:` per section. Neither, or both, is a
  config error; the existing "missing required field(s)" error style should
  name the offending section and both accepted forms.
- An empty `commands:` list is a config error (equivalent to neither).
- `commands:` run sequentially in list order. The first failing command stops
  the sequence; **its** output (stdout + stderr) is the failure feedback fed
  back to the agent, exactly as the single-command path does today.
- All output of the sequence goes to the same `build.log` / `test.log` as
  today; a run of multiple commands stays within one attempt header.
- The existing `command_timeout_minutes` applies **per command** (each shell
  invocation), matching current per-invocation mechanics.
- A plain `command:` config keeps working unchanged — the hello-website
  example (`examples/hello-website/.magnaflow/config.yml`) must run as-is.

Internal shape is the implementer's choice; the natural move is to normalize
both forms to a list at load time (`command: x` → `[x]`) so `TaskRunner` only
ever loops over a list. Do not add config knobs beyond the contract.

## Acceptance criteria

- [ ] `command:` (string) and `commands:` (list) both parse for `build` and
      `test`, in any combination across the two sections.
- [ ] Neither / both / empty-list per section yields a clear config error
      naming the section; no run starts.
- [ ] Multiple commands run sequentially; first failure stops the sequence and
      its output becomes the agent feedback for that attempt.
- [ ] Single-command configs behave byte-for-byte as before (regression:
      existing `ProjectConfigTests` / `TaskRunnerTests` still pass, adjusted
      only where they touch the internal property shape).
- [ ] New unit tests cover: list parsing, both-forms error, neither error,
      empty-list error, sequential stop-on-first-failure feedback, and
      multi-command success.
- [ ] `dotnet build` and `dotnet test` pass for the WorkerController solution.
