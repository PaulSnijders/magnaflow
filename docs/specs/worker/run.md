# Technical

`mf-worker run <cmd-id>` executes one named command end to end and
stops. This is the entry point mf-watch dispatches
(`mf-worker run --project <root> <id>`). The loop itself is
[worker run](../concepts/worker-run.md); statuses and files are in
[command lifecycle](../concepts/command-lifecycle.md).

## Usage

```text
mf-worker run <cmd-id> [--project <path>]
```

- `<cmd-id>` is the command id `NNNN-name` (or `NNNNB-name`), matched
  exactly and case-sensitively. It is not the filename: `0007-cmd-x` and
  `0007-cmd-x.md` are "unknown command". Unlike `resume:`, there is no
  filename normalization.
- `--project`: a project root holding `docs/prompts/` and
  `.magnaflow/config.yml`. Default: the current directory.
- The named command must be `ready`. `run` is not a way to force a
  `draft`, `questions` or `running` command.

## Order of refusals

Config first, then the work-branch guard, then the id lookup, then the
run's own [preconditions](../concepts/worker-run.md#preconditions). An
invalid or missing `.magnaflow/config.yml` therefore wins over an unknown
id. `build` and `test` are required, each as `command:` or `commands:`
but not both. See [machine and project config](../concepts/machine-config.md).

## Exit codes

Subset of [design](../concepts/design.md#shared-conventions):

| Code | Meaning |
|---|---|
| 0 | `done`, or paused at `questions` (a pause is not a failure) |
| 1 | ended `aborted` |
| 2 | config missing or invalid, unknown id, malformed command, missing `specs:` file, unresolvable `resume:`, no base branch |
| 3 | refused, nothing changed: dirty tree, on a work branch, not `ready`, or `mf-run stop` failed (status reverted to `ready` and committed) |
| 4 | git or agent unavailable, detached HEAD, or a git failure mid-run (the command may be left `running`, or terminal but uncommitted) |

mf-watch treats 0 and 1 followed by a terminal or `questions` status as
a finished run, and anything else as a failed dispatch. These meanings
are therefore a contract.

## Output

Progress goes to stdout in grey, prefixed `[<id>]`. Diagnostics from the
command layer and the lost-terminal-commit report go to stderr,
prefixed `mf-worker:`. The run's own precondition refusals currently
print on stdout. Scripts must use the exit code, not stderr.

BUG: argument parsing is Spectre's default rather than
[design](../concepts/design.md#shared-conventions). An unknown option is
silently ignored, and a missing `<cmd-id>` or unknown subcommand exits
with Spectre's own code (observed 127), not 2.

DRAFT: generated from code, not human-reviewed.
