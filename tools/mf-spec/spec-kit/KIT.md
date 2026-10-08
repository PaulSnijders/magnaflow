# spec-kit — version 1.3

A self-contained copy of the spec system: one markdown spec per page,
grouped per surface; `concepts/` for cross-cutting logic; a `# Technical`
split between user help and developer notes; the spec-first rule; a
drift audit (`/spec-drift`) with a format lint (Node, no dependencies);
the prompt lane (`docs/prompts/`) and two record genres
(`docs/decisions/`, `docs/context/`). Design rationale lives in the
magnaflow repo under `tools/mf-spec/`.

- **Adopt**: copy this folder into the target repo as `docs/spec-kit/`
  and run `0001-adopt-spec-system.md` there as a prompt in Claude Code
  (it derives the surface config from the repo).
- **Update**: copy the folder again and run
  `0002-update-spec-system.md`.
- Delete the copy when done — and any other copy of the kit (a folder
  holding this `KIT.md`) that ended up somewhere in the repo: a stray
  copy carries its own `docs/CLAUDE.md`, which agents load as if it were
  the project's. The master lives in the magnaflow repo
  (`tools/mf-spec/spec-kit/`); improvements flow back there.

The version is also stamped in `docs/specs/README.md`; the update prompt
reads that stamp. Repos on any 0.x or 1.x version are brought to 1.3
in one run of `0002-update-spec-system.md`.

1.1: context is managed, not only appended — one conversation is one
file, corrections and cleanup are expected, substance stays (see
`docs/context/README.md`). The earlier "never updated; new information
is a new file" rule scattered one conversation over many fragments and
kept mistakes standing.

1.2: code quality is steered through what the kit already has
(decision 0017 in the magnaflow repo). design.md carries engineering
principles next to appearance, so `/architect` checks every cmd against
them; adoption asks once whether to set up a strict build/linter or
only advise, and records the answer in design.md's Quality baseline;
a quality pass per PR (or monthly on `main`) with the built-in
`/security-review` and `/code-review`, triaged by a human and filed as
`docs/context/YYYY-MM-DD-quality-pass.md`; `/spec-drift` shows the date
of the last pass and flags one older than 30 days.

1.3: weight. Docs cost reading — for the human reviewing and for the
agent's context — and every word in a spec is one the next change has
to keep true. `/spec` no longer drafts by "adding what is missing" from
code (that retold the code: field lists, schema concepts copying the
entities); `# Technical` aims for under ~800 words. A decision is one
choice in a few hundred words; investigations stay in the rst or
`context/`. `spec_lint` reports specs that link into scratch folders.
Adopt and update remove every stray copy of the kit.

The kit is tool-neutral: it installs the spec system and nothing else.
The MagnaFlow hooks (`.magnaflow/config.yml` for the worker, its
gitignore lines) are one optional question in the adopt and update
prompts. It coexists with GitHub Spec Kit: we own `docs/specs/` and
`/spec*`, they own `specs/` and `/speckit.*`.

## Contents

| Kit file | Goes to | Owner after adoption |
|---|---|---|
| `docs/specs/README.md` | `docs/specs/README.md` | kit (overwritten on update) |
| `docs/specs/config.yml` | `docs/specs/config.yml` | project (derived once) |
| `commands/spec.md`, `commands/spec-drift.md`, `commands/cmd-go.md` | `.claude/commands/` | kit |
| `skills/specs/SKILL.md` | `.claude/skills/specs/SKILL.md` | kit |
| `skills/brainstorm/SKILL.md`, `skills/architect/SKILL.md`, `skills/cc-review/SKILL.md` | `.claude/skills/<name>/SKILL.md` | project (starting point, adapt to the project) |
| `scripts/spec_lint.mjs` | `scripts/spec_lint.mjs` | kit |
| `docs/CLAUDE.md` | `docs/CLAUDE.md` | kit (merged if customised) |
| `docs/decisions/README.md`, `docs/context/README.md` | same paths | project once created |
| `CLAUDE-section.md` | merged into `CLAUDE.md` | re-merged on update |

## Deliberately not in the kit

Considered and rejected, so the next adopter does not re-derive them.
Each guards something that only matters at dozens of files or in a real
PR workflow, and until then costs more than it returns. Every check is
also something that can fail for the wrong reason.

| Rejected | Why |
|---|---|
| A docs CI gate (`.github/workflows/docs.yml`) | Pays off only with a team and a PR flow. The lint runs locally through `/spec-drift`; the STATUS.md date says when nobody ran it. |
| `naming-lint` as a CI check | Guards a mistake that happens once or twice a year; the folder README catches it cheaper. |
| `docs-root-allowlist` | The genre table already keeps the `docs/` root small. A linter here adds a failure mode without adding a rule. |
| `spec-missing` as a CI check | `/spec-drift` already reports it, and a missing spec needs a judgement call — a person, not a red build. |
| `spec-stale-in-pr`, and its pre-commit variant | Assumes a PR workflow with a reviewer. In a repo that commits straight to `main` it is friction you switch off within two weeks. The STATUS.md expiry replaces it. |
| Commit-message rules with a `Spec:` trailer | Pays off only through the check above, which is not there. |
| Audit-state frontmatter in STATUS.md (`audited_at`, `unaudited_commits`, `freshness`) and `ACCEPTED.md` | Sha bookkeeping in every report to make "stale" precise. A 14-day date on the `Generated:` line catches the same neglect, and re-running on a quiet repo is seconds. Accepting a finding hid an enforcement gap instead of fixing it. |
| A mandatory `Date`/`Topic`/`Status` header on records | At a handful of files per folder, grep on the content works; the frontmatter the READMEs ask for is the minimum for that grep. |
| An index README per folder | `ls` is the index until a folder passes roughly fifteen files. |
| A `TEMPLATE.md` per genre | The folder README is a dozen lines — the agent writes the file from it. |
| A `/decision` command | Creating one file with a sequence number does not need a command. |
| A `/quality` command or quality scores | Overlaps the built-in `/security-review` and `/code-review` and adds a report to ignore; LLM-assigned scores are noisy. The quality pass uses the built-ins (decision 0017). |
| A `/spec-fold-in` command | Folding a delta into the specs is ordinary spec-first work with the diff in front of you; a command only made it look like a separate phase. |
| `HANDOVER.md` | Overlaps almost entirely with CLAUDE.md; the cadence belongs there. |
| Year subfolders in `context/` | Below about three years they add a level without adding findability. |
| Optional "layers" of the kit (core + magnaflow) | The MagnaFlow coupling is one question in two prompts; a layer structure around it is more machinery than what it isolates. |
