# Why the conventions are what they are

The kit's `docs/specs/README.md` is re-read every session, so it states
the rules and nothing else. This note holds the reasoning behind them
(it sat next to each rule up to kit 0.14 and moved here in 1.0). Read
it when you want to change a rule, not when you want to follow one.
How the system works: [system.md](system.md).

## Five genres in five folders

Specs and records cannot share a folder, even on the same subject,
because their lifecycles are opposite: a spec is rewritten to describe
today; a record stands still so it still says what was thought at the
time. Mix them and either the record gets "updated" into a history that
never happened, or the spec freezes into a changelog. The genre table
gives every new document a home before it is written — anything
without one lands in the `docs/` root and rots there.

"Records do not mutate" is a direction, not a lock. A record that
points at moved files, has a mistranscribed figure, or holds a secret
is corrected without ceremony. A record whose *content* no longer holds
gets a successor. Each folder README draws the line.

## The concept threshold is low on purpose

The first rule was "a concept only when two or more pages would
otherwise duplicate the explanation". In two months of real use that
produced zero concepts while six cross-cutting mechanisms were built: a
rule that asks you to predict future duplication is a rule nobody
applies. Since 0.12 the threshold is positive: a mechanism that
determines behavior and cannot be read off one module gets a concept,
even if one page shows it.

## Concept and decision are split strictly

The concept carries the outcome and one `Why:` line; the decision
carries the argument. Put the weighing in the concept and it drifts
away from the reasoning that justified it — a year later the file
explains a choice it no longer describes. Put the current truth in the
decision and it must be edited every time behavior changes, which makes
it a concept in the wrong folder.

## Help absence must be explicit

A title with an empty help block makes "forgotten" and "deliberately
none" look the same. Forcing the help-less form to *start* with
`# Technical` makes the choice visible and mechanically checkable. It
also lets rendering fail closed: users see help only for the full
two-h1 form, so a malformed marker can never leak admin content. And it
is greppable: `grep -l '^# Technical' -m1` lists all admin-only specs.

## Numbers are read off disk

A thread parked and picked up hours later has a stale view of
`docs/prompts/`. The number it believes is next has usually been taken
by work that ran in between, and two prompts then collide. Reading the
highest number on disk costs one `ls`. The same rule applies to
`docs/decisions/`.

## No central status file

State lives in each artifact's frontmatter, and overviews are
generated. A central list is the first thing to drift, because it is
edited apart from the thing it describes. `/spec-drift` writes "Open
prompts" into STATUS.md, and `grep -l "status: ready"` answers "what
can be picked up" at any moment. A list would add a second truth and no
information.

## The `summary:` line on reports

The rst body is written for the next AI session and is as long as that
needs; a human scanning the lane wants one sentence. The frontmatter
line is that sentence, and a dashboard renders it under the command. A
report without one simply renders without it — nothing enforces the
length.

## Self-answered questions go in the report

A plan file used to hold the questions the executor answered for
itself. Nobody read the plan file, so nobody saw that a command was
under-specified. Writing those questions into the rst, where a human
already looks, turns them into feedback on the cmd: a long list means
the command should have said more.

## Bookkeeping commits are prefixed

Status transitions and rst/qa updates without code are real commits
(git is the database), but they are noise in a history review and must
not trigger a pipeline. A prefix plus `[skip ci]` gives
`git log --invert-grep` a handle. The prefix was `mf:` until 0.14 and
became `lane:` in 1.0: the kit also runs on projects without MagnaFlow,
and a prefix should name what it marks (the delta lane), not the tool
that happens to read it.

## No branch by default

For a solo trunk workflow the PR gate is friction with no reviewer
behind it, so a branch is a per-change opt-in. When a change does earn
one, bookkeeping stays on the invoking branch (normally trunk) so the
trunk always shows the live queue and every report. A checked-out work
branch is a frozen snapshot; updating statuses there makes the trunk
lie.

## Freshness in days, not in audited commits

Kit 0.12 to 0.14 stamped STATUS.md with `audited_at`,
`unaudited_commits` and `freshness`, and kept a hand-owned
`ACCEPTED.md` of findings a human had waved through, each pinned to the
sha it was judged at. The argument was precise: an audit goes stale
through code movement, not time, and a report that keeps re-raising
dismissed items trains you to skip it.

1.0 replaced all of it with a 14-day rule on the `Generated:` line, for
two reasons:

- Reacting to an expired date is cheap. On a quiet project
  `/spec-drift` comes back green in seconds — a confirmation, not a
  false alarm. The sha bookkeeping put a small state machine in every
  report and in the prompt that writes it.
- Accepting a finding is the wrong move under spec-first. A finding is
  an enforcement gap; the fix is at the gap, not in a list of things to
  stop mentioning.

A dashboard that wants the precise number counts it itself
(`git rev-list --count --since=<date> -- <code roots>`).

## Line endings are pinned

With `core.autocrlf=true` (the Git for Windows default) a fresh clone
checks docs out as CRLF. The same file then differs between a Windows
and a Linux machine without anyone editing it, and any script that
reads the working tree sees different bytes. The v1 lint failed outright
on this; the 1.0 lint normalises on read. The `.gitattributes` step
stays in the adopt prompt anyway, because the diff noise and the
cross-machine mismatch are the real problem, not the lint.

## Things the kit does not do

Rejected ideas and their reasons are in the table "Deliberately not in
the kit" in [KIT.md](spec-kit/KIT.md), so they do not creep back in.
