---
description: Pick up the next ready cmd from docs/prompts/ and execute it
argument-hint: "[NNNN]"
---

Execute one cmd from the lane — the hand-run counterpart of the
MagnaFlow worker. Run it in a fresh session (not under `/architect`,
which does not implement).

1. **Current lane.** `git pull` first, so you see the lane as it is now
   (another machine or the worker may have moved it). Stop on a
   conflict and report it.
2. **Pick.** With an argument (`$ARGUMENTS`, e.g. `0027` or `0021B`):
   that cmd. Without: list `docs/prompts/*-cmd-*.md` on disk, read each
   frontmatter `status:`, take the `ready` one with the lowest number.
   The picked cmd must be `ready`; otherwise name its status and stop.
   No `ready` cmd: say so and stop — never pick a `draft`.
3. **Announce.** One line: number, title, and how many other cmds are
   still `ready`.
4. **Claim.** Set `status: running`, commit `lane: NNNN running
   [skip ci]` and push before any other work, so a second executor
   cannot pick the same cmd. Push rejected: pull, re-read the status,
   and continue only if it is still yours.
5. **Execute** per the lane conventions in `docs/specs/README.md`
   (frontmatter, rst shape, branches):
   - Read the cmd in full, and the `rst` of the cmd named in
     `resume:`/`group:`, if set.
   - Create `NNNN-rst-name.md` and keep it up to date as you go — it is
     the report the next conversation reads.
   - With `branch:`: bookkeeping (cmd status, rst, qa) commits on the
     branch you were started from; code and its spec updates on the
     work branch, created from `base:`. Never change prompt statuses on
     the checked-out work branch.
   - Update the owning specs with the code, in the same commit.
   - Before `done`, run the verification the cmd names — its tests,
     plus the build/test commands in `.magnaflow/config.yml` if the
     repo has one.
6. **Finish** with one of:
   - `done` — implemented, rst written, specs updated;
   - `questions` — a decision that is genuinely the human's: write the
     questions to `NNNN-qa-name.md` (they answer beneath each and set
     the cmd back to `ready`);
   - `aborted` — reason in the rst.

   Commit everything (status/rst/qa-only commits as `lane: … [skip
   ci]`) and push — both branches with `branch:`. Do not open a pull
   request; that and merging are the human's. One cmd per run: do not
   start the next one; report the result and the paths written.
