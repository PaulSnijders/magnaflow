---
description: Create or update the spec for a page or concept
argument-hint: <surface>/<route> (e.g. web/about), or concepts/<slug>
---

Create or update the spec file for: $ARGUMENTS

Follow the `specs` skill and `docs/specs/README.md`. This command is for
one-time drafts from code (brownfield onboarding, new pages) and for
explicit spec work orders — routine spec updates happen inline with code
changes per the spec-first rule, not through this command.

If the argument names a concept (`concepts/<slug>`), the file is
`docs/specs/concepts/<slug>.md`: ground it in the modules on its `Code:`
line (read them), update Help/`# Technical`, fix the `Code:` line to the
real owning modules, and refresh the one-line links from page specs that
touch it. Skip the route steps below.

Steps:
1. Resolve the route to its code using `docs/specs/config.yml`: the
   surface's `root` + `routes` glob + `slug` rule, plus the components
   and endpoints the page uses. Read them.
2. Resolve the route to its spec path: `docs/specs/<surface>/<slug>.md`.
3. If the spec exists, update it: keep wording that is still correct,
   fix what the code contradicts, add what the code cannot say. If not,
   write it from scratch — choosing the right form: user-facing unit →
   title + help + `# Technical`; admin page or technical concept → start
   directly with `# Technical` (no help, no title). Never describe
   appearance in prose — reference `concepts/design.md` and record only
   deviations.
   Drafting from code tempts you to retell it. Write down decisions,
   reasons, quirks and the contracts that must stay stable; a list of
   fields, columns, endpoints or steps the code already shows becomes a
   symbol name on the `Code:` line, not prose. Aim for a `# Technical`
   under ~800 words; past that, look for retelling before adding more.
4. Update `docs/specs/STATUS.md`: remove this unit from the exception
   lists if present, add one dated line under "Recent spec updates"
   (trim that section to the last ~10 entries — git holds the full
   history). Create STATUS.md with the README's section structure if
   missing.
5. Show a summary of what changed. On a real mismatch between existing
   spec and code, call it out and ask which side is the truth.

If no argument is given, list units without a spec (per surface globs)
and ask which one to do.
