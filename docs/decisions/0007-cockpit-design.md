---
date: 2026-07-13
topic: cockpit
status: accepted
---

# 0007 — mf-cockpit v0.1 — design

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

The "glasbox" dashboard from the phase-1 brainstorm and
`docs/context/2026-07-07-cockpit-multi-project-idea.md`: with several
MagnaFlow projects and a worker on another machine, nothing answered
"who is at bat?" in one glance. All state already existed as plain text
on disk (lane, evidence, watcher log, specs, git); the gap was a place
to see it, drill down, ask questions, and give the occasional "go"
without a terminal on the worker machine.

## Options

1. **A read-only viewer.** Safe, but every action (flip a draft to
   ready, commit a hand-edit) still needs an editor or SSH on the
   machine that holds the copy.
2. **A window with buttons.** Renders the files; offers a small, growing
   set of writes, each one something a human could do by hand.
3. **An actor.** The cockpit (or its chat) executes work itself, next
   to the worker.

## Measurement

The tool principles (concepts/design.md): plain text is the interface,
git is the database, small tools that compose by spawning. Whatever the
cockpit does must stay reconstructable from the files without it, and
work must keep happening in exactly one place, the lane. Option 1 failed
the practical need; option 3 creates a second place where work happens
and state that lives only inside the cockpit.

## Choice

Option 2, under one invariant.

### Invariant: buttons, not an actor

Everything the cockpit shows stays readable without it (`cat`
suffices), and every action it offers is something a human could also
do at the file/git level — the same rule as the worker and mf-watch.
The invariant is not "read-only", it is:

- every write is an explicit **human-initiated** action (a click),
  never a decision the cockpit takes on its own;
- every write lands in the existing plain-text/git formats — no state
  that exists only inside the cockpit;
- the cockpit never touches run evidence (`.magnaflow/<id>/`) and never
  edits specs.

The `.magnaflow/` clause was re-scoped in v0.3 to mean exactly the run
evidence; see 0008-cockpit-run-and-config.md. The one store outside git,
the scratchpad, is argued in specs/cockpit/scratchpad.md.

v0.1 started with two writes, both in `docs/prompts/`: create a `draft`
cmd, and flip exactly `draft` → `ready`. Every later button was added as
another explicit write under the same rule. Writes that land in git are
committed at once (`cockpit: ...`): the worker's dirty-tree guard is right
to refuse uncommitted lane files, and on a `git_sync` machine mf-watch's
next push is the outbox.

### Why particular later writes look the way they do

Only the reasoning no spec carries; behavior is in the page specs.

- **Commit all** (`git add -A`, the one deliberate exception to "no
  other file"): it is the "commit a hand-edit" action v0.1 deferred.
  Its default message is the name of the one spec file among the
  pending changes when there is exactly one — spec-first means a
  behavior change and its spec commonly land together; zero or several
  is ambiguous, so no default.
- **Pull and Switch branch** *are* git, so they need no extra commit of
  the cockpit's own. Pull is `--ff-only` only, with prompts disabled and
  a timeout so a credentials prompt fails fast.
- **Switch branch** exists for the Linux worker: the lane is branch
  state, and that machine has no terminal in the merge flow, only the
  cockpit over Tailscale. A machine left on the old branch finds no
  ready commands and says nothing — a silent failure, which is what
  makes it worth a button. The listed branches are also the whitelist
  the checkout validates against, so no caller-supplied text reaches a
  git argument. `git switch`, not `git checkout`: for a name that exists
  only as `origin/<name>` it creates the tracking branch itself, and it
  cannot silently detach HEAD. After a switch the page warns but does
  not refuse while a watcher runs: a poll in flight cannot be detected
  (the watch lock is process-lifetime), and refusing would disable the
  button on the one machine it exists for.
- **Check now** writes `.magnaflow/mf-watch.wake`, which is not
  evidence. It is not committed (a transient signal mf-watch deletes on
  sight; `touch` is the by-hand equivalent), and it is not on
  `IWatchControl`, because creating a file needs no platform split.

### The chat

A read-only chat, not an agent: headless Claude Code with fixed args
that config may narrow but never widen. Its way into action is "make
this a command" — a `draft` in the lane. The chat never executes work;
the lane does, so there stays one place where work happens.

### Smaller choices

- **Static pages over an SPA**, each fetching JSON; splitting pages is
  free because state lives in files, not in the page. No build step and
  no CDN, because it must work on a LAN without internet.
- **The cockpit watches files where mf-watch polls.** mf-watch polls to
  keep one code path everywhere; the cockpit runs on one machine over
  local paths, and a missed event costs only a stale page, never
  correctness.
- **Where it runs**: anywhere with a working copy, no machine detection;
  typically the worker machine (freshest copy, always on), viewed over
  Tailscale/LAN.

### Not in scope (v0.1)

Auth/HTTPS, historical metrics or charts, parallel-run awareness beyond
a busy indicator, mobile layout, tray/daemon packaging (use the OS's
service mechanism). Further buttons (commit, branch, retry, answering qa
in the browser) were named as natural v0.x candidates, each to be added
as an explicit write under the invariant.

Current behavior: tools/mf-cockpit/README.md and specs/cockpit/
(index, project, command, chat, specs, config, scratchpad);
appearance in concepts/design.md.
