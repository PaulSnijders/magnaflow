---
title: "Optional test gate and a separate agent timeout"
cmd: 0032-cmd-optional-test-gate.md
done: 2026-11-09
summary: "mf-worker now treats test: as optional (absent key = no test gate, the agent owns the tests) and has defaults.agent_timeout_minutes for the agent alone; all 249 worker tests pass. Once the worker has self-updated, Triferto Forecast can drop its test: and set agent_timeout_minutes in its own config (your edit)."
---

# 0032 — Optional test gate and a separate agent timeout

## What changed

**mf-worker (`tools/worker-controller/`)**
- `Config/ProjectConfig.cs`
  - `test:` is optional. Key presence is found by parsing the root a second time with `YamlStream` (`HasRootKey`).
  - With no key, `TestCommands = []`.
  - With the key present, it goes through the existing `ResolveCommands`. So a bare `test:`, `test: null`, `test: {}` or a blank `command` gives `test.command or test.commands`, and `commands: []` gives `test.commands is empty …`. Both use the same "missing required field(s)" message that `build` gets.
  - `build` is unchanged.
  - New `AgentTimeout`, from `defaults.agent_timeout_minutes`. It falls back to the resolved `command_timeout_minutes` (default 30). A value below 1 gives `"<path> has an invalid value: defaults.agent_timeout_minutes must be at least 1"`.
  - `WithCommandTimeout` is renamed `WithAgentTimeout`. It sets only `AgentTimeout`; `CommandTimeout` is copied unchanged.
- `Agents/ClaudeCodeRunner.cs` uses `config.AgentTimeout`. `Actions/ActionRunner.cs` calls `WithAgentTimeout(action.Timeout)`.
- `Execution/TaskRunner.cs`
  - `hasTestGate = TestCommands.Count > 0`.
  - The `test.log` writer is opened only when `hasTestGate` is true (`using` with a nullable writer). Its attempt header is written with `?.`.
  - With no gate, a green build logs `attempt i/n: no test gate (test: not configured)` and passes the attempt.
  - With a gate, the path is byte-for-byte the old one.
  - Both "timed out after N minutes" messages now report `AgentTimeout`.
- `Execution/PromptBuilder.cs`
  - `Preamble` is now `Preamble(bool hasTestGate)`.
  - `BuildPlan` and `BuildInitial` each gained one optional trailing parameter, `hasTestGate = true`. All existing callers are unaffected.
- Tests: 249 pass (`dotnet test tools/worker-controller/WorkerController.slnx`).
  - New `ProjectConfigTests`: absent test key; a theory over 5 present-but-unusable forms; no build; the agent-timeout cases (default 30, fallback 45, 60 vs its own `CommandTimeout`, 0 is malformed); `WithAgentTimeout`.
  - New `TaskRunnerTests`: no gate gives `done` in 1 attempt, only `build-cmd` is spawned, there is no `test.log`, the log line appears, and the prompt carries the no-gate rules line. A red build with no gate still retries.
  - New `PromptBuilderTests`: both rules-line variants, each for the plan and the implementation prompt.
  - New `ActionRunnerTests`: the agent factory receives `AgentTimeout` = the action's 9 min while `CommandTimeout` stays 20.

**Docs and kit**
- `tools/mf-spec/spec-kit/skills/magnaflow/config.md`: the YAML example, the "`build` and `test`" section and "`defaults`", including the `worker_timeout_minutes` line.
- `tools/worker-controller/README.md` and `EXAMPLES.md`: the config block, the "test is optional" wording, a new "No test gate" recipe, and a note that `test.log` exists only with a gate.
- I followed `0002-update-spec-system.md`.
  - Only `skills/magnaflow/` differed from the master. It is copied to `.claude/skills/magnaflow/`.
  - The other installed copies (`docs/specs/README.md`, the commands, `skills/specs`, `spec_lint.mjs`, `docs/CLAUDE.md`, the CLAUDE.md managed section) already matched the master, so they are unchanged.
  - `.magnaflow/` was not touched.
  - I rebuilt `spec-kit.zip` with the root `CLAUDE.md` recipe. That recipe's `git add` left `tools/mf-spec/spec-kit/` staged.
  - No version bump.
- Specs
  - `concepts/worker-run.md` step 5: the log line, and that the plan prompt carries the same rules line.
  - `concepts/machine-config.md`: `test: null` / `test: {}` and the "invalid value" message.
  - `concepts/evidence-layout.md` and `cockpit/command.md`: `test.log` may be absent.
  - `STATUS.md`: a Recent spec updates entry.
  - `spec_lint`: 0 problems.

## Checked, no change needed
- Cockpit: `EvidenceReader` reads `test.log` through `TailReader`, which already returns `Exists: false` / an empty tail for a missing file. That is the same path a plan-only run takes today.
- The worker has no other `test.log` reader, so the `RunAll` and status output are unaffected.

## Decisions while implementing
- I changed one existing test, `Load_ListsMissingRequiredFields`. Its config has no `test:` key, so it now asserts that the error does **not** contain `test.command`, where it used to require it. This follows directly from the new contract. The existing TaskRunner tests are unchanged.
- `AgentTimeout` has a nullable backing field whose getter falls back to `CommandTimeout`. So a `ProjectConfig` built in code without `AgentTimeout` (`TestSupport.TestConfig`, 1 min) keeps the old agent limit. That mirrors the loader's fallback.
- I did not add the cockpit "no test gate" summary text (out of scope).

## Self-answered questions
- **Error text for `agent_timeout_minutes < 1`?** It gets its own message, `has an invalid value: defaults.agent_timeout_minutes must be at least 1`, because "missing required field(s)" would be wrong for it. It is checked after missing fields are reported.
- **Which "kind of message" for `test: null` / `test: {}`?** The existing `test.command or test.commands` message from `ResolveCommands`. `build` gets that same message for the same shapes.
- **Does the plan prompt also get the no-test-gate rules line?** Yes. Both prompts share the preamble, and a plan that promises a test gate that doesn't exist would mislead the agent. The spec now says so.
- **Should a config built in code fall back to `CommandTimeout` too, or default to 30?** It falls back to `CommandTimeout`, which matches the loader and keeps the existing test configs' behaviour.
- **Does the `git add` in the zip recipe count as a forbidden git operation?** No. Only commit, branch, checkout and push are forbidden. The recipe's `git add`/`write-tree` only staged the kit folder.
