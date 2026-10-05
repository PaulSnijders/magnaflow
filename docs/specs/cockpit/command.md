# Technical

`command.html?p=<name>&id=<NNNN[B-Z]-name>` tells one command's whole
story: the four lane files, the evidence, and the two writes that make
sense from here. The id is the lane id without the file type, e.g.
`0005B-fix-lava`. An unknown id is a 404, shown in place of the heading.
Appearance follows [design](../concepts/design.md#shared-conventions).
See [command lifecycle](../concepts/command-lifecycle.md) for the files
and statuses.

## Header

Title, status pill (`malformed` when the cmd file does not parse), and
one meta line: id, attempts (`n/max` when `max_attempts` is set), branch,
group, and the parse error if any. The tab title leads with the status
icon, then `id · project`.

Under the meta line sits the rst's `summary:`. It is plain text, never
markdown-rendered, and shown in full here, whereas the lane row truncates
it. It is not repeated above the Report, whose frontmatter block already
carries it. An rst without the field renders nothing.

**Make ready** appears only for a `draft`. It flips exactly
`draft → ready`, the same rule as the lane's Ready button: the server
answers 409 for any other current status and commits only that file
(`cockpit: ready <id>`).

## Lane files

Command, Plan, Questions and Report. The Command card shows the cmd
body only, because its frontmatter is the header. A malformed cmd shows
the raw file instead, so it stays diagnosable. Other files' frontmatter
renders as a key/value block, the body as markdown. Links to sibling
lane files (`./NNNN-{cmd,pln,qa,rst}-name.md`) are rewritten to this
page; every other link opens in a new tab.

## Evidence

The recorded session id, then tails of `claude.log`, `build.log` and
`test.log` from `.magnaflow/<id>/`. Each tail is bounded (last 200 lines,
64 KB). Each has a Pretty/Raw toggle: Pretty renders JSON lines (the
agent's stream-json) as collapsible blocks. The page never writes
there. Evidence is machine-local, so a command run on another machine
shows empty tails here. See
[evidence layout](../concepts/evidence-layout.md).

## Continue on this command

Shown only once the story is over (`done` or `aborted`). Feedback is
required (400); the slug is optional. Ctrl+Enter submits. It creates a
follow-up draft:

- **Id**: the parent's base number plus the next free letter `B..Z`.
  Every file with that base number counts, of any type and
  well-formed or not. A follow-up of `0005B` is `0005C`. All letters
  taken is a 409.
- **Content**: title `<parent title> (follow-up)`, slug defaulting to
  the parent's name. It copies `group`, `specs`, `branch` and `base`.
  The body is the feedback, then a link back to the parent cmd and its
  rst.
- **`resume:`** is the parent's recorded agent session id (from its
  `session.yml`), so the worker continues the same session. Without
  one, the draft is still created, without `resume:`, and the page
  alerts "no recorded session — will start cold". See
  [session resume](../concepts/session-resume.md).

It is committed alone (`cockpit: create follow-up draft <id> (from
<parent>)`) and the browser moves to the new draft.

The endpoint enforces what the card shows: a parent that is not `done`
or `aborted` is a 409 with an `error` naming its status. An unknown
parent is a 409 as well.

## Live updates

`lane` and `evidence` events for this project refetch the whole page
(the summary bar on `lane`). Evidence from *any* command in the project
triggers it; the refetch is cheap.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0007-cockpit-design.md
