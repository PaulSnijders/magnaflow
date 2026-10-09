# Technical

`project.html?p=<name>` is the working page for one project: what is
running, the lane, adding to the lane, git, and the watcher. Card order is
the reading order and is deliberate. Appearance follows
[design](../concepts/design.md#shared-conventions).

The page is a window, not an actor. Every write goes through an endpoint
that enforces the same rule the button shows, and the server answers 409
when the rule fails. Buttons explain a disabled state in their `title`
before they are pressed.

## Action row

**Check now** writes `.magnaflow/mf-watch.wake`. A running watcher polls
within about 1 s instead of finishing its backed-off sleep. The button is
enabled only while a watcher runs, because nothing would consume the
file otherwise. One late refetch follows, because the log append does
not reliably raise a file event on Windows.

## Run

Shown only when the project has `run:` services or a `run.stable`. It
offers Start all and Stop all (hidden when there are no services), and
per service Start/Stop/Restart plus a log tail of 100 lines. Every read
and click shells out to [mf-run](../run/mf-run.md).
The page tracks no process state itself.

The state combines mf-run's two facts:

| tracked | port listening | shown as |
|---|---|---|
| yes | yes | running |
| no | yes | running (not started by mf-run) |
| yes | no | process up, port not answering |
| no | no | stopped, with the reason |

**Stable row**, only when `run.stable` is configured
([stable instance](../run/mf-run.md#stable-instance)). It sits below the
services. It shows the process state as above, plus mf-run's `state`
(building / ready / failed with its `message`), the short `sha` with
`dirty`, and `at`. **Behind** is marked when `sha` differs from the
checked-out HEAD. The link is `link` shown verbatim when it is set, or
otherwise `url` with the usual loopback-host rewrite. Buttons:
Start/Stop/Restart (service name `stable`), log, and **Promote**. Promote
spawns `mf-run promote` detached and returns at once, so the cockpit's
mf-run timeout never cuts a publish short. The row follows `building`
through the normal refetch. Promote is refused (409) while a command is
`running`, because the publish would read a tree the agent is editing,
and while `state` is `building`. Start all and Stop all never touch
stable. A project without `run.stable` gets the card, and the API
response, it always had.

## Lane

Columns: Id, Title, Status, Attempts, Duration, Branch, action. Newest
first, 10 at a time. The server's ordinal order (`0005 < 0005B < 0006`)
is reversed only at render time, so a follow-up sits above its parent
(marker ↱). Duration comes from the first line of `claude.log` up to the
last evidence write. It renders as `m:ss` under an hour and `h:mm:ss`
above. A `draft` row offers **Ready**, which flips exactly
`draft → ready`. See [command lifecycle](../concepts/command-lifecycle.md).

Sound cues announce terminal transitions over the *full* lane, so a
command below the fold still chimes. They are silent on first render. At
most one cue plays per refetch, with priority aborted > questions >
done.

## New draft command

Title and body produce `docs/prompts/NNNN-cmd-<slug>.md` with
`status: draft`, the next free number, committed immediately.
Ctrl+Enter submits.

## Git

This card is loaded separately from the fast data, because it spawns git
(which takes seconds on a large untracked tree). A git error never
blocks the rest of the page.

- **Branch switch** runs `git switch <branch>` to a branch from the
  listed set. The lane is branch state, so moving a machine between
  work branches is a checkout and nothing more. It is refused on a dirty
  tree or while a command runs. After the switch it warns, but never
  refuses, when a watcher or services are still up.
- **↻** runs `git fetch` and reloads the list. It is the only control on
  the page that reaches the network.
- The header shows the branch, dirty or clean, and `↑N ↓M`: commits
  ahead of and behind the upstream as last fetched (`git rev-list
  --left-right --count HEAD...@{u}`, local only). Without an upstream it
  is left out. A divergence shows after **↻**, or after a refused Pull,
  since a pull fetches too.
- **Pull** runs exactly `git pull --ff-only`. It is refused on a dirty
  tree or while a command runs. The button is then disabled and its
  `title` says why.
- **Sync** takes the Pull button's place when the copy is both ahead and
  behind, which `--ff-only` refuses. It runs `git pull --rebase`, then
  `git push`. Only local commits are replayed, never a merge or a
  force-push. Same guards as Pull (409, disabled with a `title`). On a
  conflict the rebase is aborted, so the tree is as it was, and the
  conflicting files are shown inline with git's own output. When only
  behind, the button stays Pull.
- **Commit all** commits everything and pushes, and is disabled on a
  clean tree. It reports one of: nothing to commit, committed (no
  remote), or committed and pushed.

git's own exit code and output are always shown inline, never
swallowed.

## Watcher

A Start/Stop toggle, the lock line (PID and start time from
`.magnaflow/mf-watch.lock`), and the log tail. The last 10 lines are
visible, "load all" shows the rest of the fetched 100, and the expanded
state survives a live refresh. See
[watch toggle](../concepts/watch-supervision.md) for the Linux
(systemd) versus Windows (spawn/kill) split.

## Live updates

One SSE stream (`/api/events`). The lane, watcher and run cards refetch
their own data. The Git card is deliberately not on this fast path.
Expanded lists and log tails survive a refetch.

The run card's status read is cached per project for a few seconds, so
that every open page can refetch it on one event. A change to
`.magnaflow/stable/state.yml` drops that cache before its `run` event,
so the stable row follows building → ready without waiting out the
cache. The files a publish writes under `.magnaflow/stable/` raise no
event.

DRAFT: generated from code, not human-reviewed.

Why: decisions/0007-cockpit-design.md, decisions/0018-stable-instance.md
