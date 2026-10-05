# Technical

`mf-worker run-all` drains the queue: it runs every `ready` command one
by one, in id order, then stops. It is still a one-shot tool. It never
waits for new work; polling is mf-watch's job
([watch supervision](../concepts/watch-supervision.md)). Each command
runs as in [worker run](../concepts/worker-run.md).

## Usage

```text
mf-worker run-all [--project <path>]
```

## Batch rules

- **Re-scan after every command.** The next pick is the first `ready`
  command not yet attempted in this invocation. A command made `ready`
  mid-batch is picked up. No command runs twice in one batch.
- **An aborted command never stops the batch**, and neither does a
  per-command usage error (exit 2: missing spec, bad `resume:`).
- **A systemic problem stops it.** After exit 3 (dirty tree, stop
  failure) or 4 (environment), the batch ends at once with that code,
  because every remaining command would be refused the same way.
- **`group:` sharing** only exists here. Consecutive commands with the
  same group continue one agent session
  ([session resume](../concepts/session-resume.md#precedence)).
- A `questions` pause counts as success. The batch moves on, and the
  paused command waits for its human.

At the end it prints a table with one row per command run: `done`,
`aborted`, or `error (exit N)`.

BUG: the table maps exit codes, not statuses, so a command that paused
at `questions` (exit 0) is shown as `done`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | nothing ready, or every command ended `done` or paused |
| 1 | at least one command ended `aborted` (takes precedence over 2) |
| 2 | config invalid or missing, or at least one command hit a usage error |
| 3 | stopped on a precondition refusal, or refused up front on a work branch |
| 4 | stopped on an environment error |

DRAFT: generated from code, not human-reviewed.
