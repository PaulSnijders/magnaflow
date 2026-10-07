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
5. **Execute** per "When executing a cmd" in `CLAUDE.md` and the lane
   conventions in `docs/specs/README.md`: read the cmd in full (and the
   `rst` of the cmd named in `resume:`/`group:`, if set), keep
   `NNNN-rst-name.md` up to date, follow `branch:`/`base:`, update the
   specs with the code. Before `done`, run the verification the cmd
   names — its tests, plus the build/test commands in
   `.magnaflow/config.yml` if the repo has one.
6. **Finish** with `done`, `questions` (with `NNNN-qa-name.md`) or
   `aborted` (reason in the rst). Commit and push. One cmd per run: do
   not start the next one; report the result and the paths written.
