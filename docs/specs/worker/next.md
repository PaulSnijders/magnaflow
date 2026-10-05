# Technical

`mf-worker next` runs the first `ready` command in id order, exactly as
[`run`](run.md) would run it, and stops. The loop is
[worker run](../concepts/worker-run.md).

## Usage

```text
mf-worker next [--project <path>]
```

## Selection

- Queue order is the ordinal order of command ids
  (`0005-x < 0005B-x < 0006-y`); see
  [command lifecycle](../concepts/command-lifecycle.md#files-and-identity).
  There is no priority field.
- Only well-formed `ready` commands qualify. A malformed command is
  skipped silently here. `status` shows it.
- The queue is read from the checked-out branch. On a command's work
  branch, `next` refuses before reading it (exit 3).
- An empty queue prints `nothing ready` and exits 0. That is a normal
  outcome for a one-shot tool.

## Exit codes

As [`run`](run.md#exit-codes), plus 0 for "nothing ready". An unknown id
cannot occur.

`--help` describes it as "Execute the first ready command in ID order".
Argument parsing is strict, as for [`run`](run.md#argument-parsing).

DRAFT: generated from code, not human-reviewed.
