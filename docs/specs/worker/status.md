# Technical

`mf-worker status` prints a read-only table of every command in the lane.
It writes nothing: no file, no commit, no `.magnaflow/` folder.

## Usage

```text
mf-worker status [--project <path>]
```

## Table

Columns: Command, Title, Status, Attempts. Rows are in id order and
include `draft`.

- **Attempts** reads `attempts/max`. Max is the command's `max_attempts`,
  else the config default, else `-`.
- **Malformed entries** show as a row with status `!` and the title
  `(malformed: <reason>)`, highlighted. This includes orphaned pln, qa
  or rst files. A malformed command never stops the listing. This is the
  only subcommand that surfaces them; the others skip them silently.
- An empty or missing `docs/prompts/` prints
  `no commands found under <path>`.

## Deliberate tolerances

- **Works without a valid config.** `.magnaflow/config.yml` only supplies
  the max-attempts default, so a missing or broken config still lists
  the lane.
- **Works without git.** On a command's work branch it prints a note on
  stderr that the table is that branch's frozen snapshot, not the live
  queue. Without git, or on a detached HEAD, the note is skipped.
- It does not refuse on a work branch, unlike the executing
  subcommands.

## Exit codes

Always 0. A status listing is never a failure. The cockpit's lane is
the richer, live view of the same files
([project page](../cockpit/project.md#lane)).

DRAFT: generated from code, not human-reviewed.
