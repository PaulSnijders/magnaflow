---
title: "Optional test gate and a separate agent timeout"
status: ready
created: 2026-11-09
---

## Context

In Triferto Forecast the backend suite takes 8–10 minutes and runs twice
per cmd: the agent runs it while implementing, then the worker's `test:`
gate runs it again. The agent also sometimes hits the 30-minute limit on
an implementation attempt. The product owner decided that such a
project can make the agent own the tests. The agent can fix a red test
in place, while the worker's gate can only start a whole new attempt.

Two changes to mf-worker (`tools/worker-controller/`):

1. `test:` becomes optional in `.magnaflow/config.yml`. Absent means no
   test gate.
2. The agent's timeout gets its own key. Today
   `defaults.command_timeout_minutes` governs both the agent and every
   build/test command (`Agents/ClaudeCodeRunner.cs` uses
   `config.CommandTimeout`). That means a long agent limit cannot be
   set without also lengthening the limit on a hung build.

The specs are already written ahead:
`docs/specs/concepts/worker-run.md` (step 5, "Attempts and timeouts")
and `docs/specs/concepts/machine-config.md` (the project config table).

## Task

1. **Config** (`Config/ProjectConfig.cs`):
   - `test:` is optional. When the **key is absent**, `TestCommands` is
     empty. When the key is present in any form without a usable
     command (`commands: []`, a blank `command`, a bare `test:` /
     `test: null`, `test: {}`), the config is malformed with the same
     kind of message `build` gets today, so a typo never switches
     testing off. The current DTO cannot tell a bare `test:` from no
     key, so detect key presence explicitly (for example, also load the
     root as a mapping and check for `test`). Keep it small. `build:`
     stays required, with exactly today's rules.
   - New `defaults.agent_timeout_minutes` → a new `AgentTimeout`
     property. When unset it falls back to the resolved
     `command_timeout_minutes`, which still defaults to 30. A value
     below 1 is malformed. `CommandTimeout` keeps its meaning for
     build/test only.
   - `WithCommandTimeout` (used by `Actions/ActionRunner.cs` for a
     `prompt` action) becomes `WithAgentTimeout` and sets
     `AgentTimeout`. An action keeps running under its own
     `timeout_minutes`.
2. **Agent** (`Agents/ClaudeCodeRunner.cs`): use `config.AgentTimeout`.
   **TaskRunner** (`Execution/TaskRunner.cs`): the two "timed out after
   N minutes" messages report `AgentTimeout`.
3. **No test gate** (`Execution/TaskRunner.cs`): with `TestCommands`
   empty, an attempt whose build is green succeeds. Skip the tests
   step, log one line `attempt i/n: no test gate (test: not configured)`
   in place of `...: tests`, and do not create `test.log`. Only open
   its writer when there are test commands. Check that the cockpit's
   command page and `RunAll`/status output have no problem with a
   missing `test.log` (a plan-only run already has none).
4. **Prompt** (`Execution/PromptBuilder.cs`): the rules line "The
   project's build and tests will be run after you finish implementing;
   they must pass." depends on the config. With tests it is unchanged.
   Without tests: "Only the project's build is run after you finish
   implementing; it must pass. There is no test gate: you own the
   tests. Run the project's tests yourself and make them pass before
   you finish." Pass what the builder needs (a bool is enough). Do not
   reshuffle its signatures beyond that.
5. **Tests** (xUnit, `tests/MagnaFlow.WorkerController.Tests/`):
   - `ProjectConfigTests`: no `test:` key → loads, `TestCommands`
     empty; `test: { commands: [] }`, a bare `test:` and a blank
     `test.command` → malformed, with the error naming `test`; no
     `build:` → still malformed; `agent_timeout_minutes` unset → 30;
     unset with `command_timeout_minutes: 45` → 45; set to 60 → 60 while
     `CommandTimeout` stays at its own value; `0` → malformed.
   - `TaskRunnerTests` (existing fakes, `TestSupport` config builder
     with an empty test list): no test gate → green build = `done` in
     one attempt, no test command spawned, no `test.log`; red build
     still retries; a config with tests behaves exactly as today
     (existing tests stay green unchanged).
   - `PromptBuilderTests`: both variants of the rules line.
   - `ActionRunnerTests`: a prompt action's agent gets the action's
     timeout as `AgentTimeout`.
6. **Docs and kit**, in the same commit:
   - `tools/mf-spec/spec-kit/skills/magnaflow/config.md`: in the YAML
     example, mark `test:` optional and add `agent_timeout_minutes`.
     Rewrite the "`build` and `test`" section: `build` is required;
     `test` is optional. Without it the worker runs only the build and
     a green build passes, so the agent owns the tests. That suits a
     slow suite the agent runs anyway, and the project's `CLAUDE.md`
     should then say how to run the tests. An empty `test:` is an
     error, not "off". In "`defaults`": `command_timeout_minutes` is
     the limit per build/test command; `agent_timeout_minutes` is the
     limit per agent invocation and defaults to
     `command_timeout_minutes`. Add one line: raising it may need
     mf-watch's `worker_timeout_minutes` (machine config, default 120)
     raised too, since that bounds the whole run.
   - `tools/worker-controller/README.md` / `EXAMPLES.md` wherever they
     describe the config or say test is required.
   - Then run `tools/mf-spec/spec-kit/0002-update-spec-system.md` on
     this repo and rebuild `tools/mf-spec/spec-kit.zip` as the root
     `CLAUDE.md` says. No kit version bump (0031 changed this skill
     without one).
   - Specs: the two above are written. Fix them where the
     implementation had to choose, and add a Recent spec updates entry
     in `docs/specs/STATUS.md`.

## Must not change

- A config with `test:` behaves exactly as today: same order, same
  feedback, same logs.
- `build:` rules and messages.
- `command_timeout_minutes` for build/test commands, and every config
  without the new key (the fallback keeps their agent limit).
- This repo's own `.magnaflow/config.yml`, and no other project's.

## Verify

- `dotnet test tools/worker-controller/WorkerController.slnx` is green.
- After the run (human): the Linux worker on paul-ux550 self-updates
  from the `tools/` commit once no cmd is `running`. Then Triferto
  Forecast can drop its `test:` and set `agent_timeout_minutes` in its
  own config. That edit is the human's.

## Not in scope

- The cockpit summary card showing "no test gate" when `test:` is
  absent. It already parses an absent `test:` as an empty list.
- Validating `command_timeout_minutes` or `max_attempts` (left as
  they are).
