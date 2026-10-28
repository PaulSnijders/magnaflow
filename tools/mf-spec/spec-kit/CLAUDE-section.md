<!-- The adopt and update prompts place everything from the begin
     marker to the end marker inclusive into the target repo's
     CLAUDE.md, verbatim; an update replaces that span. Project rules go
     outside the markers. This comment is not copied. -->

<!-- spec-kit:begin — managed by the spec kit; edits inside are replaced on update -->
## Specs (spec-first)

State specs live in `docs/specs/` — conventions in `docs/specs/README.md`
(read it before touching specs or behavior), this project's deviations
from the kit in `docs/specs/PROJECT.md` (they win); the design plane
(lane, records) in `docs/CLAUDE.md`.

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
<!-- spec-kit:end -->
