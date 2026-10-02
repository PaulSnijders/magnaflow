---
title: "mf-worker: a plan file only when there are questions"
cmd: 0015-cmd-plan-file-only-on-questions.md
done: 2026-09-03
summary: The worker now writes a pln only when a round has an open question (or to clear a stale one); the plan itself stays in the agent session, and self-answered questions moved to the rst. Kit 0.14. The live end-to-end check on a real project is still yours to run.
---

## What was done

**`PromptBuilder.BuildPlan`** — the plan is now produced in the agent's
reply, in the session that goes on to implement it, and the pln file is
written only when the round has something genuinely open. The builder
checks on disk whether a pln or qa already exists for the command and
emits one of two closing paragraphs:

- **no earlier pln/qa** — "If nothing is genuinely open, write no
  `NNNN-pln-name.md` at all and end your response after the plan."
- **a pln/qa exists** (the re-plan case) — rewrite that same file with
  the current plan and *without* any "Open questions" section, spelling
  out why: a leftover section pauses the run again and the command is
  never implemented.

The `## Open questions (this round)` heading is quoted verbatim in the
prompt — it is the contract `PlnFile.OpenQuestionsSection` matches on.
The "read the qa first" instruction is kept and now only appears when a
qa actually exists. `## Self-answered questions` is gone from the pln.

**`PromptBuilder.BuildInitial`** — the rst instruction asks for what was
done, the decisions taken while implementing and anything skipped or
uncertain (the kit's own wording for an rst), instead of "deviations
from the plan — do not restate the plan itself", which had no referent
left once there is normally no plan file. It also asks for a
`## Self-answered questions` section: every question the cmd left open
that the executor resolved itself, with the answer it settled on, and an
explicit "there were none" when that is the case. The `summary:`
frontmatter block from 0009 is untouched.

**`PlnFile`** — xml-doc only; behaviour unchanged. `ReadOpenQuestions`
already returns empty for an absent file and `Write` already emits only
the sections it is given.

**Spec** (`specs/002-plan-questions-feedback/`):

- FR-009 restated: the plan lives in the plan step's reply; the pln is
  written only for a round with an open question or as a re-plan of an
  existing pln/qa; self-answered questions go to the rst.
- FR-014 now states that a re-plan which resolved everything must not
  retain the previous round's open-questions section.
- FR-017 gained the self-answered questions and lost "deviations from
  the pln"; FR-019 reworded (the pln exists precisely for a paused run).
- User Story 1 (body, independent test, both acceptance scenarios), User
  Story 3 (body, independent test, scenarios 1 and 4), SC-001, the
  plan-crash edge case, and the pln/rst key entities all follow.
- The 2026-07-08 clarification that introduced the pln is amended in
  place with a dated note rather than rewritten — the same pattern the
  clarification above it already uses.
- `data-model.md` (not named in the cmd, but part of the same spec set
  and directly contradicting the new FR-009/FR-017) got the same
  treatment: the pln table lost its self-answered row, the rst table
  gained one.
- `contracts/file-formats.md`: the pln block now shows plan + open
  questions only, with when-it-is-written spelled out; the rst block's
  "Decisions and deviations from the plan" became "Decisions taken while
  implementing" and gained a `## Self-answered questions` section.

**Docs** — `tools/worker-controller/README.md`: the three lifecycle
bullets and the layout tree comment (`only when the plan raised a
question for you`). `EXAMPLES.md`: step 3 of the `run` walkthrough now
says no pln is written for a well-specified command, and step 4's
inspection list `ls`-es the pln to show it is absent instead of `cat`-ing
it.

**Cockpit** — verified: `LaneScanner` uses `SiblingIfExists` and
`command.html` already renders an empty state, so nothing was broken by
an absent pln. Only the wording changed: "no plan yet" implied one was
coming, it now reads "no plan file - this command raised no questions".

**Kit** — `docs/specs/README.md` describes the rst as also carrying the
questions the cmd left open that the executor answered itself, "how you
find out a command was under-specified". Version stamp and `KIT.md`
bumped, with a migration entry (documentation-only, repair: none).

**Tests** — `BuildPlan_InstructsTheAgentToWriteItsOwnPlnFileAndNotGuess`
is replaced by two: one asserting the no-pln-unless-open instruction and
the exact `## Open questions (this round)` heading, one driving the
re-plan branch (pln + qa on disk) and asserting the rewrite-without-the-
section wording. A third asserts `BuildInitial` asks for the
self-answered section. The `TaskRunner` gate tests were left exactly as
they are. 157 worker tests and 288 cockpit tests pass.

## Decisions taken while implementing

- **The controller decides whether a pln/qa exists, not the agent.**
  `BuildPlan` does the `File.Exists` check and picks the wording. The
  agent could have been told to look for itself, but then the crucial
  re-plan rule ("drop the stale section") would be conditional advice it
  might skip; making it a separate, unconditional paragraph in the
  re-plan prompt is the whole point of the second clause.
- **The kit went to 0.14, not 0.13.** The cmd asked for a `0.12 → 0.13`
  entry, but 0013 already shipped 0.13; the entry is `0.13 → 0.14` and
  both stamps moved with it.
- **`data-model.md` was included** even though the cmd named only
  spec.md and the contracts — it stated the opposite of the new FR-009
  in a table, inside the same spec set.
- **The spec's Clarifications record was amended, not rewritten** — it
  is a dated record of a session; a marked "Amended 2026-09-03" sentence
  keeps the history honest without leaving a contradiction standing.

## Self-answered questions

- Q: Should the pln/qa existence check live in the controller or be left
  to the agent? → A: The controller, see above.
- Q: The cmd asks for a `0.12 → 0.13` kit entry, but the kit is already
  at 0.13. → A: Added `0.13 → 0.14` and bumped both stamps; the intent
  (a new documented version so `0002-update-spec-system.md` carries the
  change into adopted repos) is what matters, not the number.
- Q: Does the rst contract keep a heading named after the plan? → A: No
  — renamed to "Decisions taken while implementing", matching the kit's
  own description of an rst, since "the plan" no longer names a file a
  later reader can open.
- Q: Should the other 002 artifacts (`plan.md`, `tasks.md`,
  `research.md`, `quickstart.md`) be updated too? → A: No. They are
  process history from the retired Spec Kit experiment. `data-model.md`
  was the exception because it carries live per-file contracts.
- Q: What should the cockpit's empty state say? → A: "no plan file -
  this command raised no questions", making the absence meaningful
  instead of pending.

## Not done

The **"Verify live"** section — running a well-specified and then a
deliberately ambiguous command end to end through `mf-worker` on a real
project — was not run. It launches an agent that writes and commits code
in another repository, which is not something to start unasked. Nothing
in it is blocked; it is ready to run whenever you want to drive it.
