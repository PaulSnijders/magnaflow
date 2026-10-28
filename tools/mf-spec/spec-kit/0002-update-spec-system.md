# 0002 — Update an already-adopted spec system

The `docs/spec-kit/` folder next to this prompt is a **newer copy** of
the spec kit; this repo already adopted an older version. Bring the
installed system files up to date without touching anything
project-specific. Every step is idempotent: running this prompt twice
changes nothing the second time.

1. **Read the installed version** from the comment at the top of the OLD
   `docs/specs/README.md`: `spec-kit version: 1.x`, or the older form
   `mf-spec kit version: 0.x` (no comment at all = 0.1). Note it for the
   summary; the repairs below cover every path to 1.5. Apply every
   "From X to Y" section whatever the stamp says: each one checks the
   repo's content, not the version, so a stamp from a fork of the kit
   cannot make a step look done.

2. **Overwrite** (system-owned, safe to replace). Before replacing a
   file, compare the installed copy with the new kit's. A difference
   that is not just older kit wording is a project edit (`git log -p`
   on the file shows which commits were kit updates): move it, as a
   rule, into `docs/specs/PROJECT.md` (create it first, per step 4)
   under a heading named after the section or file it changes, and
   list each move in the summary. Only then overwrite. Once a project's edits live in
   PROJECT.md this finds nothing.
   - `spec-kit/docs/specs/README.md` → `docs/specs/README.md`
   - `spec-kit/commands/spec.md`, `commands/spec-drift.md` and
     `commands/cmd-go.md` → `.claude/commands/`
   - `spec-kit/skills/specs/SKILL.md` → `.claude/skills/specs/SKILL.md`
   - `spec-kit/scripts/spec_lint.mjs` → `scripts/spec_lint.mjs`
   - `spec-kit/docs/CLAUDE.md` → `docs/CLAUDE.md`

3. **Replace the managed section in CLAUDE.md.** Replace the span from
   `<!-- spec-kit:begin … -->` to `<!-- spec-kit:end -->` inclusive with
   the same span from `spec-kit/CLAUDE-section.md`, verbatim. Touch
   nothing outside the markers. No markers yet (installed before 1.5):
   see "From 1.4 to 1.5". Then count the words of the root CLAUDE.md.
   It is loaded into every agent turn, and the MagnaFlow worker puts it
   at the top of every run's prompt. Above ~1,000 words, name the
   largest sections in the summary as candidates to move into a nested `CLAUDE.md`, a spec or a README
   — advice only; the rest of CLAUDE.md is the project's.

4. **Ensure present** — create only if missing, never overwrite:
   - `docs/specs/PROJECT.md` from `spec-kit/docs/specs/PROJECT.md`.
   - `docs/decisions/README.md` and `docs/context/README.md` from
     `spec-kit/docs/<name>/README.md`. Once a folder exists its README
     may have been adapted to the project and its files are history:
     leave everything in it alone, including an old `TEMPLATE.md` —
     except the one README repair in "From 1.0 to 1.1" below.
   - `.claude/skills/brainstorm/`, `architect/` and `cc-review/`
     (`SKILL.md` each) from `spec-kit/skills/<name>/`, and a `.scratch/`
     line in `.gitignore` for the brainstorm notes.
     Once present they are the project's (adapted to it): leave them alone.
   - `docs/prompts/` with a `.gitkeep`, and no `.gitignore` rule that
     excludes it, `.claude/` or `docs/specs/`.
   - `.gitattributes` with `*.md text eol=lf`, `*.mjs text eol=lf`,
     `*.yml text eol=lf` — add only the lines that are missing. See
     `0001-adopt-spec-system.md` step 4 for the warning (do not
     normalise CRLF source code) and the clean-tree re-checkout recipe.

5. **Remove retired commands** if present: `.claude/commands/decision.md`
   and `.claude/commands/spec-fold-in.md`. Their job needs no command —
   a decision is one numbered file per `docs/decisions/README.md`, and
   folding a delta into the specs is ordinary spec-first work.

6. **MagnaFlow?** Ask one question: does this project run with the
   MagnaFlow tooling? If not, skip, and leave an existing
   `.claude/skills/magnaflow/` alone (never delete it). If yes, apply
   `0001-adopt-spec-system.md` step 5: create `.magnaflow/config.yml` if
   missing (never touch existing values), ensure every runtime ignore
   line of step 5b (`!.magnaflow/actions.yml` is new in 1.4), and
   **overwrite** `.claude/skills/magnaflow/` from
   `spec-kit/skills/magnaflow/` (kit-owned, like `skills/specs/`; step
   2's compare applies, project edits go under `## magnaflow`).

   Kits before 1.0 shipped three narrower lines
   (`.magnaflow/mf-watch.log`, `.magnaflow/mf-watch.lock`,
   `.magnaflow/run/`) and committed the `<id>/` run evidence. Replace
   those three lines with the lines of step 5b, and if `.magnaflow/<id>/`
   is tracked, untrack it in its own commit: `git rm -r --cached
   .magnaflow` then `git add .magnaflow/config.yml`. Nothing is lost —
   logs and session ids stay on disk, and a session id was only ever
   usable on the machine that produced it.

7. **NEVER touch** (project-owned): `docs/specs/config.yml`, all specs
   under `docs/specs/`, `docs/specs/STATUS.md`, `docs/specs/ACCEPTED.md`,
   `docs/specs/PROJECT.md`, `docs/prompts/`, and the contents of `docs/decisions/` and
   `docs/context/` (the context README repair below excepted). One
   more exception: `docs/specs/concepts/design.md` may get a missing
   section added per "From 1.1 to 1.2" below — never a rewrite of its
   existing text — and `docs/decisions/README.md` and the architect
   skill get the bullets of "From 1.2 to 1.3".

## From 1.4 to 1.5

Customisations survive updates. Two things change once:

- **Markers in CLAUDE.md.** Before 1.5 the kit's section was merged in
  by hand, without markers. Find the spec-system text in the root
  CLAUDE.md — usually a `## Specs (spec-first)` heading and the
  paragraphs under it, but an earlier merge may have spread or reworded
  it, and older kits left "When executing a cmd", "Quality pass" and
  "Records live beside the specs" blocks (see "From 1.2 to 1.3"). Remove
  every line that restates a kit rule (spec-first, STATUS.md, `/spec`,
  `/spec-drift`, the lane and executing a cmd, records, language,
  `lane:` commits) and put the marked span in its place, or at the end
  of the file if the lines were scattered. A project-specific line among
  them — a rule the kit does not have, a deviation the project chose —
  stays, outside the markers, or moves to `docs/specs/PROJECT.md` if it
  changes a kit file's rule. List each kept or moved line in the
  summary.
- **`docs/specs/PROJECT.md`** is created (step 4), and step 2's compare
  moves earlier edits of kit files into it — including a customised
  `docs/CLAUDE.md`, under `## docs/CLAUDE.md`.

## From 1.3 to 1.4

The optional MagnaFlow skill. Nothing to migrate beyond step 6: on yes
it adds the `!.magnaflow/actions.yml` ignore line and installs
`.claude/skills/magnaflow/`; on no nothing changes.

## From 1.2 to 1.3

Weight. The overwritten README, `/spec` and `spec_lint` carry most of
it; two project-owned files get a line added, never a rewrite:

- `docs/decisions/README.md`: if it has no rule on size or on
  investigations, add the kit's two bullets ("One choice, a few hundred
  words" and "An investigation is not a decision") after the frontmatter
  bullet. If it has no "The bar is high" paragraph, put the kit's
  paragraph after the opening one, in place of its old "Only choices a
  future reader would ask…" sentence.
- `.claude/skills/architect/SKILL.md`: if its decision-record step does
  not say "one choice per record", append the kit's sentence (one
  choice, a few hundred words; the design goes in the spec edits, an
  investigation in the rst). If that step does not start with "usually
  none", replace its opening up to "must not sprawl" with the kit's
  wording, and rename "Medium (default for anything with a decision in
  it)" to "Medium (default for anything that needs a cmd)".

Existing specs and decisions are left alone. If the new lint check
reports specs linking into scratch, list them in the summary; promoting
or dropping is the project's call. Say in the summary which specs are
over ~800 words in `# Technical` — as information, not a task.

The CLAUDE section shrank to what every session needs. Two blocks left
it; remove them from CLAUDE.md when placing the marked span, whatever
their wording:

- **"When executing a cmd"** (set `running`, keep the rst, qa and
  `questions`, `done`/`aborted`, branch bookkeeping, push both). It now
  lives in `/cmd-go`, overwritten in step 2. CLAUDE.md keeps one pointer
  line. The worker reads CLAUDE.md too, and its own rules forbid the
  agent to touch the cmd file or commit — the old paragraph contradicted
  them in every run.
- **"Quality pass"** (the section with `/security-review` and
  `/code-review`). It now lives in `docs/specs/README.md#quality-pass`;
  CLAUDE.md keeps one pointer line.

Also drop the "Records live beside the specs" paragraph if it is there:
`docs/CLAUDE.md` and the folder READMEs carry it. Keep any
project-specific sentence that was added to these blocks — move it to a
project section rather than delete it.

## From 1.1 to 1.2

design.md now carries engineering principles and a Quality baseline
(`0001-adopt-spec-system.md` steps 7 and 6). Read the installed
`docs/specs/concepts/design.md`:

- **No engineering principles** (architecture/layering, dependency
  policy, security basics, reuse before new code)? Ask the user whether
  to add them. If yes, derive them from the code as in `0001` step 7,
  show them, and add the section once confirmed.
- **No `Quality baseline` section?** Ask the user whether to add it. If
  yes, run `0001` step 6 (its one question: set it up, or advise only)
  and add the section with the outcome.

Add a section only; never rewrite text that is already there. A section
that exists — whatever its wording — means the question was answered.

## From 1.0 to 1.1

Context is managed, not only appended. If the installed
`docs/context/README.md` still carries the append-only rule ("never
updated", "new information is a new file"), replace that rule with the
kit's new bullets — one conversation is one file, correcting is expected,
cleaning up is allowed, the substance stays, the maintenance window, the
diff is the account — and keep any project-specific adaptations. Do not
merge or rewrite existing context files as part of the update; the new
rules apply from the next time someone works in the folder.

## From 0.x to 1.0

The steps above are the whole migration; no per-version repairs remain.
Two things are left as they are on purpose:

- **STATUS.md** keeps whatever frontmatter it has (`audited_at`,
  `unaudited_commits`, `freshness`). The next `/spec-drift` writes the
  1.0 form — no frontmatter, a `Generated: <date>` line that counts as a
  finding when older than 14 days.
- **ACCEPTED.md** is no longer read. Say in the summary that its
  entries are back on the table: a finding a human still considers
  fine is fixed at its enforcement gap, and the file can then be
  deleted by hand.

Then: show a short diff summary of what changed in the system files,
delete the `docs/spec-kit/` copy and any other copy of the kit in the
repo (a folder holding `KIT.md`), and commit with message
`chore: update spec system`.
