# Why the conventions are what they are

The kit's `docs/specs/README.md` is re-read every session, so it states
the rules and nothing else. This note holds the reasoning that used to
sit next to each rule (up to kit 0.14) and was moved out in 1.0. Read
it when you want to change a rule, not when you want to follow one.

## Five genres in five folders

Specs and records cannot share a folder even when they are about the
same subject, because they have opposite lifecycles: a spec is rewritten
to describe today, a record stands still so that it still says what was
thought at the time. Put them together and either the record gets
"updated" into a version of history that never happened, or the spec
freezes into a changelog. The genre table exists so a new document has a
house before it is written — anything without one lands in `docs/` root
and rots there.

"Records do not mutate" is a direction, not a lock. A record that points
at moved files, carries a mistranscribed figure, or holds a secret is
corrected without ceremony; a record whose *content* no longer holds
gets a successor. The line between the two is what each folder README
says.

## The concept threshold is low on purpose

The first version of the rule was "a concept only when two or more
pages would otherwise duplicate the explanation". In two months of
real use that produced zero concepts while six cross-cutting mechanisms
were built — a rule that asks you to predict future duplication is a
rule nobody applies. Since 0.12 the threshold is positive: a mechanism
that determines behavior and cannot be read off one module gets a
concept, even if one page shows it.

## Concept and decision are split strictly

The concept carries the outcome and one `Why:` line; the decision
carries the argument. Put the weighing in the concept and it mutates
away from the reasoning that justified it — a year later the file
explains a choice it no longer describes. Put the current truth in the
decision and it has to be edited every time behavior changes, which is a
concept in the wrong folder.

## Help absence must be explicit

A title with an empty help block makes "forgotten" and "deliberately
none" look identical. Forcing the help-less form to *start* with
`# Technical` makes the choice visible and mechanically checkable, and
lets rendering fail closed: a user sees help only for the full two-h1
form, so a malformed marker can never leak admin content. The
`# Technical`-first shape is also greppable (`grep -l '^# Technical'
-m1` lists all admin-only specs).

## Numbers are read off disk

A thread that was parked and picked up hours later has a stale view of
`docs/prompts/`; the number it believes is next has usually been claimed
by work that ran in between, and two prompts then collide. Reading the
highest number on disk every time costs one `ls`. The same rule applies
to `docs/decisions/`.

## No central status file

State lives in the artifact's frontmatter and overviews are generated,
because a central list is the first thing that drifts: it is edited
separately from the thing it describes. `/spec-drift` writes "Open
prompts" into STATUS.md and `grep -l "status: ready"` answers "what can
be picked up" at any moment, so a list would add a second truth without
adding information.

## The `summary:` line on reports

The rst body is written for the next AI session and is as long as that
needs; a human scanning the lane wants one sentence. The frontmatter
line is that sentence, and a dashboard renders it under the command. A
report without one simply renders without one — nothing enforces the
length.

## Self-answered questions go in the report

A plan file used to hold the questions the executor answered for
itself. Nobody read the plan file, so nobody saw that a command had
been under-specified. Writing those questions into the rst — where a
human already looks — turns them into feedback on the cmd: a long list
means the command should have said more.

## Bookkeeping commits are prefixed

Status transitions and rst/qa updates without code are real commits
(git is the database), but they are noise in a history review and must
not trigger a pipeline. A prefix plus `[skip ci]` gives
`git log --invert-grep` a handle. The prefix was `mf:` until 0.14 and
became `lane:` in 1.0, because the kit also runs on projects without
MagnaFlow and a prefix should name the thing it marks (the delta lane),
not the tool that happens to read it.

## No branch by default

A branch is a per-change opt-in, not the default, because for a solo
trunk workflow the PR gate is friction with no reviewer behind it. When
a change does earn a branch (multi-session, parallel work, wanting the
gate), the invariant is bookkeeping on the base branch and work on the
work branch, so the trunk always shows the live queue and every report.
A checked-out work branch is a frozen snapshot; updating statuses there
makes the trunk lie.

## Freshness in days, not in audited commits

Kit 0.12 to 0.14 stamped STATUS.md with `audited_at`, `unaudited_commits`
and `freshness`, and kept a hand-owned `ACCEPTED.md` of findings a human
had waved through, each pinned to the sha it was judged at. The
argument was precise: an audit goes stale through code movement, not
through time, and a report that keeps re-raising dismissed items trains
you to skip it.

1.0 dropped all of it for a 14-day rule on the `Generated:` line. Two
reasons. Reacting to an expired date is cheap — on a quiet project
`/spec-drift` comes back green in seconds, which is a confirmation, not
a false alarm — while the sha bookkeeping put a small state machine in
every report and in the prompt that writes it. And accepting a finding
turned out to be the wrong move under spec-first: a finding is an
enforcement gap, and the fix is at the gap, not in a list of things to
stop mentioning. A dashboard that wants the precise number counts it
itself (`git rev-list --count --since=<date> -- <code roots>`).

## Line endings are pinned

With `core.autocrlf=true` (the Git for Windows default) a fresh clone
checks docs out as CRLF, so the same file differs between a Windows and
a Linux machine without anyone editing it, and any script that reads the
working tree sees different bytes. The v1 lint failed outright on this;
the 1.0 lint normalises on read, but the `.gitattributes` stays in the
adopt prompt because the diff noise and the cross-machine mismatch are
the actual problem, not the lint.

## Things the kit does not do

The table "Deliberately not in the kit" in `KIT.md` lists rejected
ideas and their reasons, so they do not creep back in. The common
thread: each guards something that only matters at dozens of files or
in a real PR workflow, and each is a check that can fail for the wrong
reason.
