---
description: Audit drift between code and specs, write docs/specs/STATUS.md
---

Run a spec drift audit. Follow the `specs` skill and
`docs/specs/README.md`. Under spec-first this should be green; any
finding means the enforcement has a gap — report the finding AND name
the likely gap (e.g. a commit that touched code without a spec).

**Signal, not noise.** Only behavioral drift is a finding. Cosmetic
differences (formatting, renames, refactors with identical behavior) are
counted, never listed. A report the reader learns to skip is worse than
no report.

Steps:
0. Read the `Generated:` line of the current `docs/specs/STATUS.md`. If
   its date is older than 14 days, or the line is missing, that is
   itself a finding: the previous all-clear is out of date and nobody
   noticed. Report it as the first line of the final summary, with how
   many days overdue. It needs no section of its own; this run resolves
   it by rewriting the header.
1. Read `docs/specs/config.yml`. Per surface: enumerate units via
   `root` + `routes` glob; enumerate spec files under
   `docs/specs/<surface>/` (excluding `_group.md`).
2. Determine units without a spec, and specs without a unit.
3. For each matched pair, compare last-commit dates (git, never mtimes):
   spec vs the unit's code paths.
4. Where code is newer than spec: diff since the spec's date and judge
   behavioral vs cosmetic. Behavioral → a finding (stale — enforcement
   gap). Cosmetic → not listed; add to the cosmetic count only.
5. Where spec is newer than code: list as "Spec ahead (implementation
   pending)" — normal spec-first state, a work-order signal.
6. Concepts (`docs/specs/concepts/*.md`): parse each trailing `Code:`
   line; if any listed path changed after the spec, judge as in step 4.
   Verify `design.md` and `_overview.md` exist.
7. Check for unpaid hotfix debt: `git log --oneline --grep="^hotfix:"`
   since the newest STATUS.md update; list any not yet folded into specs.
8. List open prompts: every `docs/prompts/*-cmd-*.md` whose status is
   not `done`/`aborted`, one line each (number, name, status).
9. Run the format lint: `node scripts/spec_lint.mjs`. Its stdout is the
   body of the "Format problems" section verbatim (one `- ` bullet per
   problem, or `none`). This is mechanical validation — split-marker
   shape, surface folders vs config.yml, anchor uniqueness,
   `file.md#anchor` resolution, concept `Code:` paths — independent of
   drift; do not editorialise it. A malformed `# Technical` marker can
   leak admin-only content to all users, so a format problem outranks a
   stale spec. Because the output lands in STATUS.md verbatim, the lint
   skips STATUS.md itself — otherwise the next run would read every
   `file.md#anchor` it just reported as a broken anchor a second time.
10. Overwrite `docs/specs/STATUS.md`, keeping the "Recent spec updates"
   log (trim to ~10 entries). No frontmatter. Structure:

   ```markdown
   # Spec status

   Generated: <date> by /spec-drift

   ## Missing specs
   ## Specs without a page
   ## Stale specs (code ahead — enforcement gap)
   ## Spec ahead (implementation pending)
   ## Hotfix debt
   ## Open prompts
   ## Format problems
   ## Recent spec updates
   ```

   One line per item; empty sections get "none". End the body with the
   cosmetic count as a single line ("12 cosmetic diffs, not listed").
11. Show the same lists in the chat. Report only — no fixes; the user
   decides what to act on.
