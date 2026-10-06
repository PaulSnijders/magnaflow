<!-- Merge this section into the target repo's CLAUDE.md. -->

## Specs (spec-first)

State specs live in `docs/specs/` — conventions in `docs/specs/README.md`
(read it before touching specs or behavior).

The one hard rule: **any change that affects behavior updates the
affected spec in the same turn and the same commit.** No exceptions
except commits prefixed `hotfix:`, which add a debt line to
`docs/specs/STATUS.md` and must be folded into the specs afterwards.

Check `docs/specs/STATUS.md` for open items at the start of a session.
`/spec <route>` writes a one-time draft from code (new/missing pages);
`/spec-drift` is the audit and should always be green.

Delta work travels through `docs/prompts/` (`NNNN-cmd-name.md` +
`NNNN-rst-name.md`, statuses in the cmd frontmatter — see
`docs/specs/README.md`). When executing a cmd: set `running`, keep the
`rst-` report up to date; when a decision is genuinely the human's,
write your questions to `NNNN-qa-name.md` and set status `questions`
(they answer beneath each question and set it back to `ready`); finish
with `done` (specs updated) or `aborted` (reason in rst). All files are
committed. If the cmd sets `branch:`: bookkeeping (cmd/rst/status)
commits on the branch you were started from, code + spec updates on the
work branch (created from `base:`); push both, the human opens the pull
request and merges.

Records live beside the specs: `docs/decisions/` (why it became this
way — numbered, cited from a concept's `Why:` line) and `docs/context/`
(what the outside world said, ISO-dated). A decision that no longer
holds gets a successor rather than a rewrite. Context is managed: one
conversation is one file, corrected and cleaned up, never changed in
substance. Conventions in each folder's README; both may stay empty.

## Quality pass

When: per pull request; when the repo works on `main` without PRs,
about monthly over the commits since the last pass. Run
`/security-review` and `/code-review` (built-in Claude Code skills) on
the change; on `main`, `/code-review` on the range. Triage every
finding — real or not, one line why — together with a senior where
there is one. Record it in `docs/context/YYYY-MM-DD-quality-pass.md`:
the range reviewed, the findings with their verdict; short. Real
findings become cmds via `/architect` in the same conversation, never
automatically. Habit: `/simplify` on your own diff before opening a PR.

Language: converse in the user's language; write all artifacts (specs'
`# Technical`, prompt files, STATUS.md, commit messages) in English.
Only a spec's Help section follows its surface's `help_language`.

Bookkeeping commits (status/rst/qa changes without code) are prefixed
`lane:` and include `[skip ci]`; commits carrying code are normal.
