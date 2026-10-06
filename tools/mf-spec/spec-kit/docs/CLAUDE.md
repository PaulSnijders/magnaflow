# Design plane — working in docs/

A docs-only session (e.g. Claude Code running `/brainstorm` or
`/architect`) is the thinking half of a spec-first workflow:
implementation happens elsewhere (Claude Code or a worker) against
these same files. Full conventions:
`docs/specs/README.md` — read it before touching specs.

- Start each session by checking `docs/specs/STATUS.md` (exception
  report: drift, hotfix debt, format problems) and the lane
  (`docs/prompts/`) for commands at `ready`, `running` or `questions`.
- The state specs in `docs/specs/` are the truth about the product.
  Design work = editing them. Spec ahead of code is a work order, not
  an error.
- Changes that need implementation travel as prompt files in
  `docs/prompts/`: write `NNNN-cmd-name.md` (status `draft` while
  shaping, `ready` when complete). Pick `NNNN` by listing
  `docs/prompts/` first — highest number on disk + 1 — never the number
  this conversation remembers; a thread you resume hours later has a
  stale view of the lane. Read the `NNNN-rst-name.md` reports
  for what happened; answer questions in `NNNN-qa-name.md` and set the
  cmd back to `ready`.
- **Records** are written here too, and a docs-only session is where
  they belong: `docs/decisions/` for why something became this way
  (numbered, cited by a concept's `Why:` line) and
  `docs/context/` for what the outside world said (ISO-dated mail,
  meeting notes, a customer's question). Read the README in each folder
  before adding a file. A changed decision is a new file that
  supersedes the old one rather than a rewrite. Context is managed:
  one conversation is one file (follow-ups go into it), wrong names,
  dates and links are corrected, noise is removed — what someone said
  and a later-reversed decision stay. Each README says where the line
  runs.
- Writing rules (see README): specify decisions, not defaults; short
  beats complete; appearance by reference to `concepts/design.md`.
- **Sessions**: when you write a cmd, decide its session relation so
  the human never has to. Default: nothing — the executor starts fresh
  (everything it needs must be in the cmd, linked specs and rst
  reports; session context is an optimization, never a memory). Only
  when the new cmd genuinely builds on a previous one's in-flight
  reasoning, set `resume: NNNN-previous-name` (or a shared `group:` for
  a back-to-back cluster). Never copy these fields from an example or
  an earlier cmd out of habit.
- **Language**: converse in whatever language the user uses, but write
  all artifacts (specs' `# Technical`, cmd/rst/qa prompt files,
  STATUS.md entries) in English. The only exception is a spec's Help
  section, which follows the surface's `help_language`.
