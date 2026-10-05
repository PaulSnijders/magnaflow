# mf-spec — the MagnaFlow spec system

One compact markdown spec per page, per surface, plus concepts for
cross-cutting logic. The specs are the AI's memory (20× smaller than
the code), the user's in-app help, and the team's documentation — one
source, three uses. Spec-first: specs lead or change together with the
code, never behind.

> This folder describes the system **as it is now**. History and
> rationale live in `docs/decisions/` and `conventions-rationale.md`. The installable
> files live in `tools/mf-spec/spec-kit/` — that kit is the normative
> source for target repos.
> Full explanation: [system.md](system.md).

## Use it in a project

Two ways in:

- **Manual copy.**
  1. Copy `tools/mf-spec/spec-kit/` into the target repo's `docs/` —
     same name on both sides, no rename.
  2. Run `0001-adopt-spec-system.md` as a prompt in Claude Code. It
     analyzes the repo and proposes the surface config (you confirm
     names, `help` / `help_language`), places the files (conventions,
     commands, skills (specs, brainstorm, architect, cc-review),
     `scripts/spec_lint.mjs`, record-folder READMEs),
     merges the CLAUDE section, pins line endings in `.gitattributes`,
     asks once whether the project runs with MagnaFlow (if yes: worker
     config + the runtime ignore lines `.magnaflow/mf-watch.log`,
     `.magnaflow/mf-watch.lock`, `.magnaflow/run/` — not all of
     `.magnaflow/`; see system.md), and writes the initial specs from
     the code (calibrate the first 2–3, then batches). Existing
     hand-written help is input, not overwritten. The kit itself is
     tool-neutral (kit 1.0); it also runs on projects without MagnaFlow.
  3. Delete the `docs/spec-kit/` copy when done.
- **mf-cockpit "Add project"** (`+` on the project list) pre-copies the
  kit into `docs/spec-kit/`, pre-adds the `.gitignore` lines, and seeds
  `0001-adopt-spec-system.md` as a draft in the prompt lane — mark that
  draft `ready` and it runs the same adopt prompt above.

## Daily workflow

- Think and design in Cowork on `docs/` only; implement in Claude Code.
- **The one rule:** any behavioral change updates the owning spec in
  the same turn and the same commit. Emergency escape: `hotfix:` commit
  prefix + debt line in STATUS.md, folded in later.
- Pick the weight by risk, not by code size:
  - small (default): edit state spec → implement → done;
  - medium: a prompt pair in `docs/prompts/` — `NNNN-cmd-name.md`
    (goal, decisions, tasks; status in frontmatter: draft → ready →
    running → questions/done/aborted; after answers: back to ready) +
    `NNNN-rst-name.md` (the report the next conversation reads) +
    `NNNN-qa-name.md` when questions arise (executor asks, human
    answers beneath). All committed; overview via STATUS.md, no central
    status file;
  - exceptional (migrations, auth, payments): a full pipeline, added
    only for that feature.
- **Numbering**: before you write a new `NNNN`, list `docs/prompts/` on
  disk and take the highest number present there + 1. Never continue
  counting from a number you saw earlier in the conversation: threads
  get parked and picked up again later, and other work claims numbers
  in the meantime — that is how two `0001-cmd-*.md` files end up side
  by side. A follow-up on an existing command is not a new number but a
  letter suffix (`0008B-`).
- New page or thin area you're touching: `/spec <surface>/<route>`
  writes a one-time draft from code; deepen-on-touch and remove its
  `DRAFT:` marker when you work with it.
- Branches: none by default (work on trunk). Opt in per change via
  `branch:` in the cmd — for multi-session work, parallel work, or the
  PR gate. Bookkeeping stays on the branch the executor was started from;
  the executor pushes the work branch, you open the PR and merge.

## Commands

- `/spec <surface>/<route> | concepts/<slug>` — one-time draft from
  code (new pages, brownfield gaps) or explicit spec work order.
- `/spec-drift` — audit; should always be green. A finding = an
  enforcement gap, fix the gap. Writes STATUS.md (exception report);
  runs `scripts/spec_lint.mjs` for the "Format problems" section.
  Cosmetic diffs are counted, not listed.

## Auditing

Run `/spec-drift` when the repo has moved. Steady state: every
STATUS.md section reads "none" and its `Generated:` date is less than
14 days old — older, or missing, is itself a finding on the next run.
On a quiet project the re-run is green in seconds; that is a
confirmation, not a false alarm. Findings are fixed at their
enforcement gap, never waved through (why:
`ontwerp-conventions-rationale.md`).
