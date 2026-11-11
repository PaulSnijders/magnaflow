# Documentation volume: too many specs?
Status: direction chosen — kit 1.3 committed (c962a8d), kit 1.4 implemented, not committed

## Problem
User read the classic "lightweight docs is the sweet spot" advice and asks:
is MagnaFlow over-documenting? User's own counter: the docs are for the AI,
not for me, so volume is fine.

## Measured (2026-10-08, words)
- docs/specs 18k (≈ 1 word per line of C#; ~20.5k lines in tools/)
- docs/decisions 21k over 18 files — MORE than the specs; 0007 = 3.1k, 0013 = 2.4k
- docs/prompts 35k (lane history: cmd/rst/qa)
- docs/context 2k

## What the outside world says (2025-2026)
- SDD critique (Böckeler/Fowler, Marmelab "Waterfall strikes back", Scott Logic):
  sea of verbose markdown, review is the bottleneck, one workflow size for
  all problem sizes, false sense of control.
- ETH Zurich AGENTS.md study (arXiv 2602.11988): broad context files cost
  +20% inference and often lower success; minimal, human-written works best.
- Living-docs camp: docs in repo, updated in the same change, record what
  to believe today, not the discussion that produced it.
=> "for the AI" does not make volume free: cost moved from writing to
   (a) human review, (b) agent context/distraction, (c) rot that agents trust blindly.

## Premises (draft, not yet agreed)
1. Writing cost ~0 now; reading/review cost and context cost are the real ones.
2. State specs (spec-anchored, "decisions not defaults", drift audit) are the sweet spot already.
3. Decisions are the heavy genre: longer than the specs, and nobody re-reads 3k words.
4. Lane history is an audit trail, not loaded context — volume there is cheap.
5. Process weight (cmd+rst+qa per change) may not fit small changes.

## Answered
- decisions/ is read by agents on demand (like context), not by the user.
- User's feeling: decisions grow too fast; a file per small question,
  while the genre is meant for major choices.

## Findings on decisions/
- The rules already say the right thing: decisions/README (one choice;
  Situation/Options/Measurement/Choice; never restate current truth) and
  architect SKILL step 2 ("only for an important decision ... must not sprawl").
- The weight is legacy: 0001-0014 are per-version *design documents*
  (0013 = 9 small choices + API + scope; 0007 = whole cockpit v0.1 design),
  not one-choice records. They restate current truth -> agent may trust
  stale copies over the spec (rot risk).
- Recent pace already slowed: 0015 (489 w), 0017 (451 w) follow the format;
  0016 (1.7k) is borderline.

## Done (user asked, 2026-10-08)
Decisions 0001-0014 trimmed to the four-section format (18.4k -> 7.9k
words), duplicated behavior replaced by spec pointers. Not committed.
Spec gaps surfaced (info that lived only in decisions, kept there briefly):
- cockpit live-refresh robustness (heartbeat, watchdog, backoff, polling, badge)
- summary bar contents; ~3s memoization of git/run endpoints + /api/overview
- commit-all default message (GitCommitMessageSuggester)
- cockpit.new_project.templates schema (machine-config.md)
- git pull with GIT_TERMINAL_PROMPT=0 + timeout
- mf-spec update: record version, flag customized files
Stale code comments: WatchReader.cs cites 0006 "Observability";
Logger.cs cites ontwerp-v0.1.md (pre-existing).
Still open: 0016 at 1.7k words.

## Open questions
- Is the "too fast" feeling about old files, or still happening now?

## Other project's verdict -> kit causes (2026-10-08)
Other project: ~79k words state docs vs 88k LOC (0.9 w/line); page specs 4-6k;
database-schema.md retells entities; decisions that are investigations;
root CLAUDE.md 1.6k; kit copy left in docs/scratch/tmp; specs link into scratch/.
Kit causes found:
- /spec step 3 "add what is missing" from code -> invites retelling; no size target
  for # Technical (only Help has <300).
- decisions/README: no size hint, no home for investigations.
- CLAUDE-section.md (413 w) merged into root repeats docs/CLAUDE.md + specs/README.
- spec_lint does not flag specs linking into scratch/temp.
- adopt/update prompts do not say to remove the kit copy afterwards.
Proposed kit changes (smallest first): see conversation; rejected: hard
size limits / CI gates (KIT.md rationale), words-per-LOC as a metric.

## Item 3: CLAUDE-section.md (2026-10-08, kit 1.3 committed as c962a8d)
Facts: the worker injects the target's root CLAUDE.md into every phase prompt
(ConventionLoader, specs/concepts/worker-run.md#conventions-loading). Its
Preamble says: never touch the cmd file, never write qa, never git commit/push.
The kit's CLAUDE section says: set `running`, write qa, set status, commit,
push both branches. => contradiction in every worker run; the hand-run rules
only matter to /cmd-go, which already points back to CLAUDE.md for them.
Proposal: move "When executing a cmd" into /cmd-go; quality-pass text into
specs/README with a one-line pointer; section ~150 words: hard rule,
STATUS check, pointers, language, lane: prefix. Adopt/update report root
CLAUDE.md size (>1,000 words = advice).
Chosen (user, 2026-10-08): assume a cmd runs via the worker or /cmd-go.
Execution rules move into /cmd-go (kit-owned, overwritten on update, so it
propagates). CLAUDE section keeps ONE conditional pointer line for plain
sessions that pick up a cmd anyway: "Executing a cmd by hand (not through
the worker): follow .claude/commands/cmd-go.md." Conditional wording so it
does not contradict the worker preamble. No per-prompt reference lines.
