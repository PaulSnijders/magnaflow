# 0002 — Update an already-adopted spec system

The `docs/spec-kit/` folder next to this prompt is a **newer copy** of
the spec kit; this repo already adopted an older version. Bring the
installed system files up to date without touching anything
project-specific. Every step is idempotent: running this prompt twice
changes nothing the second time.

1. **Read the installed version** from the comment at the top of the OLD
   `docs/specs/README.md`: `spec-kit version: 1.x`, or the older form
   `mf-spec kit version: 0.x` (no comment at all = 0.1). Note it for the
   summary; the repairs below cover every path to 1.2.

2. **Overwrite** (system-owned, safe to replace):
   - `spec-kit/docs/specs/README.md` → `docs/specs/README.md`
   - `spec-kit/commands/spec.md` and `commands/spec-drift.md` →
     `.claude/commands/`
   - `spec-kit/skills/specs/SKILL.md` → `.claude/skills/specs/SKILL.md`
   - `spec-kit/scripts/spec_lint.mjs` → `scripts/spec_lint.mjs`
   - `spec-kit/docs/CLAUDE.md` → `docs/CLAUDE.md` (merge, don't
     overwrite, if the repo customized it)

3. **Re-merge `spec-kit/CLAUDE-section.md`** into CLAUDE.md — update
   the spec-system section to the new wording (the bookkeeping commit
   prefix is now `lane:`, and there is no `/decision` command), keep
   everything else in CLAUDE.md as is.

4. **Ensure present** — create only if missing, never overwrite:
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
   MagnaFlow tooling? If not, skip. If yes, apply
   `0001-adopt-spec-system.md` step 5: create `.magnaflow/config.yml` if
   missing (never touch existing values) and ensure the runtime ignore
   lines of step 5b.

   Kits before 1.0 shipped three narrower lines
   (`.magnaflow/mf-watch.log`, `.magnaflow/mf-watch.lock`,
   `.magnaflow/run/`) and committed the `<id>/` run evidence. Replace
   those three lines with the two of step 5b, and if `.magnaflow/<id>/`
   is tracked, untrack it in its own commit: `git rm -r --cached
   .magnaflow` then `git add .magnaflow/config.yml`. Nothing is lost —
   logs and session ids stay on disk, and a session id was only ever
   usable on the machine that produced it.

7. **NEVER touch** (project-owned): `docs/specs/config.yml`, all specs
   under `docs/specs/`, `docs/specs/STATUS.md`, `docs/specs/ACCEPTED.md`,
   `docs/prompts/`, and the contents of `docs/decisions/` and
   `docs/context/` (the context README repair below excepted). One
   more exception: `docs/specs/concepts/design.md` may get a missing
   section added per "From 1.1 to 1.2" below — never a rewrite of its
   existing text.

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
delete the `docs/spec-kit/` copy, and commit with message
`chore: update spec system`.
