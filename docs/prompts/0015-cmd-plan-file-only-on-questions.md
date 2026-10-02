---
title: "mf-worker: a plan file only when there are questions"
status: ready
created: 2026-09-03
---

## Context

Wozzol has run 57 commands through the worker. It has 57 pln files —
685 KB of them, against 398 KB of cmd and 448 KB of rst. The plan is
the largest body of text the lane produces and the only one nobody
reads.

Measured on that lane:

- No cmd, no spec and no config file has ever referenced a pln. Only
  rst files do — 15 of them, almost always in their opening sentence
  ("Implemented exactly per `NNNN-pln-...`"), which is not a reference
  to anything, only a claim that a plan existed.
- One genuine archival hit in 57 rounds: `0016-rst` recovered the
  correct route `/dbmodel/` from `0008-pln-funnel.md`. That same round
  is also the cost: the wrong route came from the cmd, was inherited
  into the pln, and had to be corrected months later in all three
  files. An archived plan is a document that rots and needs
  maintenance.
- 51 of 57 rst files already carry a "Deviations from the plan" or
  "Decisions taken while implementing" section. Whatever the plan did
  not survive is already recorded where a reader looks for it.
- No qa file has ever existed on that lane. 54 of 57 plns instead
  carry "## Self-answered questions"; 8 carry "## Open questions (this
  round)" that went nowhere.

And this repo's own lane, which is not worker-driven, has 24 prompt
files: cmd + rst, zero pln, zero qa. It has been running without
written plans the whole time.

**The plan file is not the handoff.** `.magnaflow/<id>/claude.log`
shows the plan phase and `attempt 1..3` running under one session id:
attempt 1 opens with ~117k cache-creation tokens, i.e. the whole plan
phase replayed into the same session. Continuity even survives round
boundaries — `0005` → `0005B` → `0005C`, six hours apart, is one
continuing session. The execution phase never opens the pln, because
the plan is already in its context. `BuildInitial`'s own wording says
it: "following the plan you just wrote".

**But the file is load-bearing for the gate.** It is the controller's
only window into the agent's session: `PlnFile.ReadOpenQuestions()`
reads `## Open questions (this round)` back out of it to decide
FR-010 (continue) versus FR-011 (pause). So this is not "stop writing
the file" — it is "write it only when it carries a question".

That is also the only moment it is ever read. `docs/mf-spec/
ontwerp-summary-line.md` already names it: *"The pln — it is read
once, at the gate, while you are deciding."* This command makes the
file exist exactly at that moment, and its presence in the tree
becomes a signal in itself: this command needed a human.

No new frontmatter field, deliberately — a per-command switch to ask
for a plan is a switch nobody would ever set.

The one thing only the pln has is "## Self-answered questions": what
the command left open and the executor decided for itself. That is
feedback about cmd quality and it is worth keeping, so it moves to the
rst, where a human already looks.

## Task

1. **`PromptBuilder.BuildPlan`** (`tools/worker-controller/src/
   MagnaFlow.WorkerController/Execution/PromptBuilder.cs`). The plan
   itself is produced in the agent's reply, not in a file. Rewrite the
   instruction so the agent writes `{pln}` **only** when either:

   - it has at least one genuinely open question this round, or
   - a `{pln}` or `{qa}` file already exists for this command.

   The second clause is not optional: it is what keeps a re-plan
   honest. Round 1 pauses and leaves a pln with an open-questions
   section; the human answers in the qa and sets `ready`; the re-plan
   must rewrite that same file and **drop** the section now that
   nothing is open. A stale section left behind re-triggers the gate
   forever and the command never implements.

   When a pln is written, the heading stays exactly
   `## Open questions (this round)` — `PlnFile.OpenQuestionsSection`
   matches on it, and it is the contract between agent and controller.
   The "## Self-answered questions" section leaves the pln (task 2).
   Keep the existing instruction to read `{qa}` first when it exists.

2. **`PromptBuilder.BuildInitial`** in the same file. Two changes to
   the rst instruction:

   - Ask for a `## Self-answered questions` section: every question
     the command left open that the executor resolved itself from the
     linked specs, the existing code or the project conventions, with
     the answer it settled on. This is what tells a human their
     command was under-specified.
   - Drop "deviations from the plan — do not restate the plan itself".
     With no plan file in the normal case, "the plan" has no referent
     for a later reader. Ask instead for the decisions taken while
     implementing and anything skipped or uncertain — which is what
     the kit's own conventions README already describes an rst as.

   The `summary:` frontmatter block from 0009 is unchanged.

3. **`Prompts/PlnFile.cs`** — the xml-doc says the pln is "the plan
   step's output every time it runs". It is now the plan step's output
   only when the round has a question. No behaviour change:
   `ReadOpenQuestions` already returns empty for a file that does not
   exist (`PlnFileTests` asserts it), and `Write` already emits only
   the sections it is given.

4. **Spec** — `specs/002-plan-questions-feedback/spec.md`: FR-009 says
   the plan and every question it raised MUST be recorded in the pln.
   Restate it: the plan is produced in the plan step's reply; the pln
   records the open questions when there are any, and self-answered
   questions move to the rst (FR-017). FR-010, FR-011, FR-011a and
   FR-015 keep working unchanged — check the surrounding prose for
   sentences that assume the file is always there. FR-019's reasoning
   ("the pln and qa files already carry that context") stays true for
   a paused run, which is the only run it applies to.

5. **Contracts** — `specs/002-plan-questions-feedback/contracts/
   file-formats.md`: rewrite the pln block (it currently shows
   Self-answered + Open questions and says "written by the plan phase
   every run") and move the self-answered shape into the rst block,
   whose body line still reads "anything decided or changed relative
   to the pln".

6. **Docs** — `tools/worker-controller/README.md` (the artifact list
   around line 50 and the tree comment at line 61, "the agent's plan
   (written before any code change)") and `EXAMPLES.md` (around 111,
   and the walkthrough step at 141 that tells the reader to `cat` the
   pln — in a well-specified example there will now be no such file,
   so that step has to say what the reader should see instead).

7. **Cockpit** — verify only, no change expected: `LaneScanner` uses
   `SiblingIfExists` and `command.html` already renders "no plan yet"
   for an absent pln. Consider that empty-state wording: "no plan yet"
   implies one is coming, where it now means the round raised no
   questions.

8. **Kit** — `tools/mf-spec/spec-kit/docs/specs/README.md` describes
   the rst as "what was done, decisions taken while implementing,
   anything skipped or uncertain". Add the self-answered questions to
   that description, and add a `0.12 → 0.13` entry to `KIT.md`
   (documentation-only; repair: none required). The kit never
   documented the pln — it is worker-owned, not human-owned — so
   nothing else there changes. This is the change that reaches adopted
   repos through `0002-update-spec-system.md`.

9. **Tests** — `PromptBuilderTests.BuildPlan_InstructsTheAgentToWrite
   ItsOwnPlnFileAndNotGuess` asserts the opposite of the new rule;
   retarget it at the conditional instruction and the exact
   open-questions heading. Add one asserting `BuildInitial` asks for
   the self-answered section in the rst. The `TaskRunner` gate tests
   drive `PlnFile.Write` directly as a fixture and stay valid as they
   are — do not weaken them.

## Verify live

Run a well-specified command end to end on a real project: no pln
appears in `docs/prompts/`, the lane goes straight to `done`, and the
rst carries the self-answered questions.

Then a deliberately ambiguous one: the pln appears carrying the open
questions, the controller copies them into the qa and sets
`questions`, `attempts` does not increment. Answer in the qa, set
`ready`, and confirm the re-plan rewrites the same pln without an
open-questions section and proceeds to implement in that run.

## Not in scope

Deleting the 57 existing plns in `wozzol2` — they are already in git
history and removing them from the tree is a separate decision.
Moving the pln to `.magnaflow/` (a folder you do not read is not
improved by being a different folder you do not read). Any per-command
frontmatter flag to request a plan. The rst `summary:` line from 0009.
The wozzol2 repo itself — the change reaches it through a kit update,
not by hand.
