# Technical

`mf-worker action <name>` runs one project action from
`.magnaflow/actions.yml` once and stops. mf-watch spawns it for due
actions and cockpit requests. A human can run it by hand. The mechanism
(file format, schedule, guards, files) is in
[actions](../concepts/actions.md).

## Usage

```text
mf-worker action <name> [--project <path>] [--trigger manual|schedule]
```

- `<name>` is the key under `actions:`, matched exactly.
- `--project`: as for [run](run.md#usage).
- `--trigger`: only recorded in the log header and `last.yml`. Default
  `manual`. mf-watch passes `schedule` for a due action.

## Order

1. Read `actions.yml`. A missing file, invalid YAML, an invalid
   definition or an unknown name gives exit 2.
2. For a `prompt` action, load `.magnaflow/config.yml` as `run` does, and
   check that the agent is available. A `script` action does not need
   `config.yml`, but its script must resolve; a script that is not found
   gives exit 2, listing the paths tried.
3. Guards: a `running` command in the lane, or a dirty tree, refuses with
   exit 3. The refusal is written to `last.yml` (`refused_at`,
   `refused`) and to stdout as exactly one line, `refused: <reason>`
   (`a command is running (<id>)` or `the working tree is dirty`).
   mf-watch reads that line. Nothing is logged to the action log.
4. Run with `timeout_minutes`: the script in the project root through
   the process seam, or the prompt through `IAgentRunner`, without a
   resume. Stdout first gets `action <name> (<trigger>): started`.
   Output is appended to the action log as it arrives, and a script's
   output is echoed to stdout too. A prompt echoes only its final text.
5. Check the tree. Dirty means failed.
6. Write the result line to the log and the run to `last.yml`, and the
   result to stdout as `action <name>: <result> — <seconds>s`.

## Exit codes

Subset of [design](../concepts/design.md#shared-conventions):

| Code | Meaning |
|---|---|
| 0 | ran, result `ok` |
| 1 | ran, result `failed` or `timeout` |
| 2 | usage error, `actions.yml` missing or invalid, unknown name, `config.yml` invalid for a prompt action |
| 3 | refused, nothing ran: a command is `running`, or the tree is dirty |
| 4 | git or agent unavailable |

mf-watch relies on these meanings.

Why: decisions/0019-actions-run-by-watch.md
