<!-- Merge this section into the target repo's CLAUDE.md. -->

## Specs (spec-first)

State specs live in `docs/specs/` — conventions in `docs/specs/README.md`
(read it before touching specs or behavior); the design plane (lane,
records) in `docs/CLAUDE.md`.

The one hard rule: **any change that affects behavior updates the
affected spec in the same turn and the same commit.** No exceptions
except commits prefixed `hotfix:`, which add a debt line to
`docs/specs/STATUS.md` and must be folded into the specs afterwards.

Check `docs/specs/STATUS.md` for open items at the start of a session.
`/spec <route>` drafts a missing spec from code; `/spec-drift` is the
audit and should always be green. Executing a cmd from `docs/prompts/`
by hand (not through the worker): follow `.claude/commands/cmd-go.md`.
Quality pass: `docs/specs/README.md#quality-pass`.

Language: converse in the user's language; write all artifacts (specs'
`# Technical`, prompt files, STATUS.md, commit messages) in English.
Only a spec's Help section follows its surface's `help_language`.
Bookkeeping commits (status/rst/qa without code) are prefixed `lane:`
and include `[skip ci]`.
