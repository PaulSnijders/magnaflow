---
title: "Worker: path-scoped build and test commands (run a check only when the cmd touched its paths)"
cmd: 0037-cmd-worker-path-scoped-checks.md
done: 2026-11-27
summary: "build/test commands: entries can now be {run, paths} and run only when the cmd touched those paths; update mf-worker on every machine before a project commits the mapping form, since an older worker rejects the config (exit 2)."
---

# What was done

- **Config** (`Config/CheckCommand.cs`, `Config/ProjectConfig.cs`): `BuildCommands` / `TestCommands`
  are now `IReadOnlyList<CheckCommand>` (`Run`, `Paths?`). An implicit conversion from `string`
  keeps every existing config, and code that builds one from strings, unchanged: a string
  entry becomes `Paths = null`. All 266 pre-existing worker tests passed without changes to
  their assertions. A `commands:` entry is parsed from `List<object>` (string or mapping).
  Invalid entries are reported as
  `<file> has an invalid value: build.commands[2].paths must be a non-empty list (...)`; problems
  are joined with `; `, and the run exits 2 through the existing load path.
- **Touched paths** (`IGitClient.GetTouchedPathsAsync`, `Execution/TouchedPaths.cs`):
  - git: `git -c core.quotePath=false diff --name-only --no-renames <start> --` plus
    `ls-files --others --exclude-standard`.
  - `TouchedPaths.From` drops the cmd's four lane files and its `.magnaflow/<id>/` folder. It
    also holds `ForcesAll` (`.magnaflow/config.yml` touched), `Runs(command)` and `Summary()`.
- **TaskRunner**: the start point is the claim commit, read only when some command has `paths`.
  Touched paths are computed:
  - once per attempt, after the agent and before the first build command;
  - when building the plan and implementation prompts, for the checks list.

  `RunSequentiallyAsync` skips untouched scoped commands and logs the skip. When the gate exists
  but every test command is skipped, the attempt passes with its own line.
- **PromptBuilder**: `BuildChecksSection` adds `# Checks after each attempt` to the plan prompt,
  the implementation prompt and the build/tests failure feedback, only when a scoped command
  exists. The preamble's test-gate rule is unchanged.
- **Cockpit** (`ProjectConfigReader`): it accepts mapping entries; the summary shows
  `npm --prefix topo run build (paths: topo/)`. Before this change a mapping entry made the
  cockpit's whole YAML parse fail silently, so the summary **and the Run card** would have gone
  blank.
- **Specs**:
  - `machine-config.md`: a new "Path-scoped commands" section and the table row.
  - `worker-run.md`: a new "Path-scoped checks" section, linked from step 5.
  - `worker/run.md`: the exit-2 note.
  - `cockpit/config.md`: the summary line.
- **Docs**: `spec-kit/skills/magnaflow/config.md`, mirrored into `.claude/skills/magnaflow/`
  (the folders are byte-identical); `spec-kit.zip` rebuilt (`git add tools/mf-spec/spec-kit` was
  run as part of that command); `tools/worker-controller/README.md`; `EXAMPLES.md` §7.
- **Tests**: worker 295 (was 266), cockpit 456, all green. `spec_lint`: 0 problems.
  - `PathScopedConfigTests`: the mixed list, every invalid form, the path matching rules.
  - `GitClientTests`, against a scratch repo:
    - committed + staged + unstaged + untracked;
    - rename;
    - ignored files;
    - lane files and the evidence folder;
    - a resumed run on a reused branch, where the earlier run's commit counts.
  - `PathScopedRunTests`:
    - a skipped command;
    - a mixed list;
    - all tests skipped;
    - a touched `config.yml` runs everything;
    - recomputed per attempt, against the claim commit;
    - no `paths` means no git call and no extra log lines;
    - the per-check prompt list.

## Final config shape

```yaml
build:
  commands:
    - node more/cmd/mf-klaas-backend.js build          # string: always runs
    - run: npm --prefix topo run build
      paths: [topo/]
test:
  commands:
    - run: node more/cmd/mf-klaas-backend.js test
      paths: [klaas-backend/, more/cmd/mf-klaas-backend.js]
```

- `run` is required, and `paths` must be a non-empty list. A path ending in `/` is a folder.
  Without the `/`, it is that exact file or that name as a folder; it is never a bare name
  prefix, so `topo` does not match `topology.md`.
- `\` is read as `/`, and a leading `./` is dropped.
- Absolute paths, `..`, and keys other than `run`/`paths` are refused.
- `command:` (singular) stays a string. Actions are untouched.

## Start point

- It is the claim commit (`mf-worker: <id> -> running`), in both branch modes.
- A resumed run (from `questions`, or a re-readied abort) makes its own claim commit, so it has
  its own start point. Every attempt of that run diffs against it, so earlier attempts of the
  same run count.
- In branch mode the diff goes from the claim commit (invoking branch) to the work branch's tree:
  - a reused branch's commits from earlier runs count, which the scratch-repo test covers;
  - a stale branch can only add touched paths, so more checks run, never fewer.
- Branchless, earlier *runs* of the same cmd are already inside the claim commit and do not
  count.

## Example attempt (console, then build.log / test.log)

```
[0042-topo-legend] attempt 1/3: agent working
[0042-topo-legend] attempt 1/3: touched paths: 3 (topo/ 2, (root) 1)
[0042-topo-legend] attempt 1/3: build
[0042-topo-legend] attempt 1/3: test 1/1 skipped (no change under klaas-backend/, more/cmd/mf-klaas-backend.js)
[0042-topo-legend] attempt 1/3: tests: all skipped, no touched paths
```

`build.log` after its attempt header:
```
touched paths: 3 (topo/ 2, (root) 1)
<output of node more/cmd/mf-klaas-backend.js build>
<output of npm --prefix topo run build>
```
`test.log`:
```
touched paths: 3 (topo/ 2, (root) 1)
skipped test 1/1: node more/cmd/mf-klaas-backend.js test (no change under klaas-backend/, more/cmd/mf-klaas-backend.js)
tests: all skipped, no touched paths
```
Both build commands ran (1/2 has no `paths`, 2/2 matched `topo/`); the worker prints a console
line only for a skip, not for a command that runs. A backend-only cmd would instead log
`attempt 1/3: build 2/2 skipped (no change under topo/)`.

## What a project must know before using `paths`

- **An older mf-worker cannot read the mapping form.** It deserializes `commands:` as a list of
  strings, so the config is a YAML error: exit 2 on every run, and mf-watch dispatches nothing.
  The same goes for an older cockpit, whose summary and Run card go blank. Update the tools on
  every machine that runs the project (the Linux self-update after this commit lands, or
  `install.ps1` by hand) **before** committing the first mapping entry.
- A skipped check counts as passed. A cmd that breaks `topo/` only through a file outside
  `topo/` (a shared script, a root `package.json`) is not caught unless that file is in
  `paths`. List shared inputs explicitly, as the example does with `more/cmd/mf-klaas-backend.js`.
- "Only commands that pass today" still holds per path: a red `topo/` check fails every cmd that
  touches `topo/`.
- A cmd that edits `.magnaflow/config.yml` runs every check.

## Notes for the next session

- **NUL-separated git output hangs `CliWrapProcessRunner`.** My first version used
  `git diff -z` / `ls-files -z`, and `RunExecutableAsync` never returned: the 30 s timeout
  reported `TimedOut`, while the same command from a shell returned at once. I reproduced it
  outside xUnit and did not pin the root cause (probably CliWrap's line-splitting
  `PipeTarget.ToDelegate`). The fix was newline output with `core.quotePath=false`. A path git
  still quotes (control character, `"`, `\`) is unquoted best-effort (`\"` and `\\` only).
  Anyone adding `-z` elsewhere will hit the same hang.
- `GitClientTests` got an async `GitAsync` helper for the new tests. The sync `Run` stays for the
  constructor.
- An empty-string entry in `commands:` (`- ""`) used to become an empty command. It is now an
  invalid entry ("must be a command string or a mapping"). No real config should have one.

## Self-answered questions

- **Start point in branch mode:** the claim commit, the same as branchless, rather than the work
  branch's tip at checkout. That makes a reused branch's earlier work count ("the earlier
  attempts' changes included"), and the error only ever goes toward running more checks.
- **What "lane files" covers:** the cmd's own cmd/pln/qa/rst plus its `.magnaflow/<id>/`
  evidence folder (the worker's own output). Other cmds' lane files and all other files under
  `docs/prompts/` do count.
- **A path without a trailing `/`:** the exact file, or that name as a folder; never a raw string
  prefix.
- **Unknown keys in a mapping entry:** invalid (exit 2), so a typo such as `path:` cannot
  silently make a check always run.
- **When touched paths are computed:** once per attempt, after the agent and before the build, so
  build artifacts that are not ignored cannot change which tests run. The prompts compute them
  again at their own moment.
- **Configs without `paths`:** no git call, no summary line, no prompt section. They behave
  byte-for-byte as before.
- **Which prompts carry the checks list:** the plan prompt, the implementation prompt, and the
  build/tests failure feedback (the touched set changes between attempts). Plan-crash and
  agent-crash feedback don't carry it.
- **Error message form:** `has an invalid value:` with entries numbered from 1
  (`build.commands[2]`), matching the existing `defaults.agent_timeout_minutes` message.
  Missing-field problems are still reported first.
- **The cockpit summary:** it shows the paths as a suffix rather than hiding them. It is one line
  in `cockpit/config.md`, and it is the only cockpit change.
