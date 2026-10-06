# mf-spec — the MagnaFlow spec system

One compact markdown spec per page, per surface, plus concepts for
cross-cutting logic. The specs are the AI's memory (about 20× smaller
than the code), the user's in-app help and the team's documentation:
one source, three uses. Spec-first: specs change before or with the
code, never after.

This file: how to use it. How it works: [system.md](system.md). Why:
[conventions-rationale.md](conventions-rationale.md). The installable
kit is `spec-kit/` — the normative source for target repos; its
contents and update steps are in [KIT.md](spec-kit/KIT.md). History:
`docs/decisions/`.

## Use it in a project

Two ways in:

- **By hand.**
  1. Copy `tools/mf-spec/spec-kit/` into the target repo's `docs/` —
     same name on both sides, no rename.
  2. Run `0001-adopt-spec-system.md` as a prompt in Claude Code. It:
     - analyzes the repo and proposes the surface config (you confirm
       names, `help` / `help_language`);
     - places the conventions, commands, skills (specs, brainstorm,
       architect, cc-review), `scripts/spec_lint.mjs` and the
       record-folder READMEs;
     - merges the CLAUDE section and pins line endings in
       `.gitattributes`;
     - asks once whether the project runs with MagnaFlow. If yes: the
       worker config plus the ignore lines `.magnaflow/*` and
       `!.magnaflow/config.yml` — everything under `.magnaflow/` is
       machine-local except the config (see system.md);
     - writes the initial specs from the code (calibrate the first 2–3,
       then batches). Existing hand-written help is input, not
       overwritten.

     The kit itself is tool-neutral (since kit 1.0); it also runs on
     projects without MagnaFlow.
  3. Delete the `docs/spec-kit/` copy when done.
- **mf-cockpit "Add project"** (`+` on the project list) copies the kit
  into `docs/spec-kit/`, adds the `.gitignore` lines, and seeds
  `0001-adopt-spec-system.md` as a draft in the prompt lane. Mark the
  draft `ready` and it runs the same adopt prompt.

## Daily workflow

- Think and design in Claude Code under a skill that keeps the session
  on `docs/`; implement in a separate run (see *Skills* below).
- **The one rule:** any behavior change updates the owning spec in the
  same turn and the same commit. Emergency escape: a `hotfix:` commit
  prefix plus a debt line in STATUS.md, folded in later.
- Pick the weight by risk, not by code size:
  - **small** (default): edit the state spec → implement → done;
  - **medium**: prompt files in `docs/prompts/`, all committed (no
    central status file; the lane itself is the overview):
    - `NNNN-cmd-name.md` — goal, decisions, tasks; frontmatter status
      draft → ready → running → questions/done/aborted (after answers:
      back to ready);
    - `NNNN-rst-name.md` — the report the next conversation reads;
    - `NNNN-qa-name.md` — only when questions arise (executor asks,
      human answers beneath);
  - **exceptional** (migrations, auth, payments): a full pipeline,
    added only for that feature.
- **Numbering**: list `docs/prompts/` on disk and take the highest
  number + 1, never a number remembered from the conversation (else two
  `0001-cmd-*.md` end up side by side). A follow-up keeps its parent's
  number plus a letter (`0008B-`).
- New page or thin area you're touching: `/spec <surface>/<route>`
  writes a one-time draft from code. Deepen it when you work with it
  and remove its `DRAFT:` marker.
- Branches: none by default (work on trunk). Opt in per change with
  `branch:` in the cmd — for multi-session work, parallel work, or the
  PR gate. Bookkeeping stays on the branch the executor was started
  from; the executor pushes the work branch, you open the PR and merge.

## Skills

Every step is a Claude Code session; the skill sets the role. The
installed skills live in `.claude/skills/` (masters in `spec-kit/skills/`).

| Step | How | What it does |
|---|---|---|
| Explore an idea | `/brainstorm` | Challenges the premise, 2–3 directions, writes a scratch note in `.scratch/brainstorm/`. No code, no specs. |
| Design the change | `/architect` | Reads specs first, decides with you, writes the spec edits, a decision record if needed, and the `NNNN-cmd-*.md` (set `ready` when you approve). Does not implement. |
| Execute | the worker (mf-watch → mf-worker), or a plain Claude Code session: "execute `docs/prompts/NNNN-cmd-*.md`" | Implements, updates specs, writes the rst. |
| Review | `cc-review` ("review cmd NNNN") | Lays the rst against the cmd, checks the code with git, exercises the app, reports. |
| Specs and behavior | `specs` | Loads automatically whenever behavior or specs change — carries the spec-first rule. |

`/brainstorm` and `/architect` only start when you type them; `cc-review`
and `specs` also trigger on their own. Small changes can skip the lane:
any session that edits code follows the one rule above.

## Commands and auditing

- `/spec <surface>/<route> | concepts/<slug>` — one-time draft from
  code (new pages, brownfield gaps), or an explicit spec work order.
- `/spec-drift` — the audit. Writes STATUS.md (an exception report) and
  runs `scripts/spec_lint.mjs` for its "Format problems" section.
  Cosmetic diffs are counted, not listed. It also shows the date of the
  last quality pass (`docs/context/*-quality-pass.md`, see the CLAUDE
  section); older than 30 days is a finding, none yet is not.

Run `/spec-drift` when the repo has moved. Steady state: every
STATUS.md section reads "none" and its `Generated:` date is under 14
days old (older or missing is itself a finding). A finding is an
enforcement gap: fix the gap, never wave it through (why:
[conventions-rationale.md](conventions-rationale.md)).
