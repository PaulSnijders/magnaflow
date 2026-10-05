---
title: "mf-cockpit: Check now moved to an action row at the top of project.html"
cmd: 0012-cmd-cockpit-check-now-top.md
done: 2026-08-26
summary: Check now sits on its own row above the Run card and is gone from the Watcher card; verified live on ProjectA over Tailscale.
---

## What was done

`Check now` left the Watcher card and now renders as a single button on
its own row on `project.html`, directly under the summary bar and above
the first card. Start/Stop stayed in the Watcher card. Endpoint,
disabled-while-the-watcher-is-stopped rule and the post-click watch
refetch are unchanged.

Implemented by hand, outside the worker lane — this report is written
from the running page, not from the diff, so it names no files.

## Verified live

`http://…:5210/project.html?p=ProjectA`, watcher running:

- the button is visible at the top of the page without scrolling;
- the Watcher card header holds only `Stop` — the button moved, it was
  not copied;
- the watcher log shows the wake round-trip working end to end:
  `14:04:47 wake requested (.magnaflow/mf-watch.wake) — polling now`,
  followed at `14:04:49` by a real poll that pulled new commits and
  spawned the worker for `0010-leesteken-tegen-antwoordwoord`.

## Open point

The row holds one left-aligned button while every other button on the
page sits at the right edge of a card header, so it reads as orphaned
rather than deliberate. Cheapest fix if it keeps bothering: right-align
the row, which puts it directly under the summary bar's own `watcher`
indicator. Not done — no prompt for it.

`docs/decisions/0007-cockpit-design.md` was already updated when the cmd
was written; nothing further was needed there.
