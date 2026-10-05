# MagnaFlow

Monorepo of small deterministic tools for a spec-first AI development platform.
MagnaFlow is built with its own spec system. Read `docs/specs/concepts/design.md`
first — its principles govern all design decisions.

## Layout

- `tools/<name>/` — one standalone C#/.NET console app per tool (own solution, src/, tests/)
- `tools/mf-spec/` — the spec system itself: `README.md` + `system.md` describe it,
  `spec-kit/` is the installable master. Not a surface in our own specs.
- `docs/specs/` — state specs of the tools: one surface per tool
  (`cockpit`, `worker`, `watch`, `run`) + `concepts/`
- `docs/decisions/`, `docs/context/`, `docs/prompts/` — the record genres and the lane
- `assets/` — logo and banner (linked into the cockpit build and the installer)

## Dogfooding notes

- After changing `tools/mf-spec/spec-kit/`, run its `0002-update-spec-system.md`
  on this repo too — the installed copies (`.claude/commands/`, `.claude/skills/specs/`,
  `scripts/spec_lint.mjs`, `docs/specs/README.md`, `docs/CLAUDE.md`) must match the master.
- Rolling out new tool code is `tools/install/install.ps1`, by hand: it stops the
  watcher that runs the worker, so it never belongs in a cmd's build/test.

## Rules of thumb

- Plain text (Markdown/YAML) + git for all state; never move files, change status in place
- Core logic gets xUnit tests; external processes go behind `IProcessRunner`/`IAgentRunner`
- KISS — no new dependencies without a concrete need
- New prompt in `docs/prompts/`: read the next `NNNN` off disk (highest number
  present + 1), never from what the conversation remembers — a resumed thread's
  view of the lane is stale. Follow-ups keep the parent's number: `0008B-`

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

Language: converse in the user's language; write all artifacts (specs'
`# Technical`, prompt files, STATUS.md, commit messages) in English.
Only a spec's Help section follows its surface's `help_language`.

Bookkeeping commits (status/rst/qa changes without code) are prefixed
`lane:` and include `[skip ci]`; commits carrying code are normal.
