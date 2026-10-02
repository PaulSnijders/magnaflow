---
title: "mf-cockpit: move Check now to an action row at the top of project.html"
status: done
attempts: 1
created: 2026-08-26
---

## Context

`Check now` (docs/prompts/0008) turns out to be the button on
`project.html` that gets pressed most often — it is how you say "pick
that up now" instead of waiting out mf-watch's backoff. It lives in the
Watcher card, which is the last card on the page, so every press costs a
scroll to the bottom first.

## Task

Move the button (move, not copy — it leaves the Watcher card) to a plain
action row on `project.html`, directly under the summary bar and above
the first card. Start/Stop stay where they are.

Behaviour is unchanged: same `POST /api/projects/{name}/watch/check-now`,
disabled while the watcher is not running, and the same watch-status
refetch after the click that the Watcher card's buttons already do. Read
the running state from the watch data the page already fetches — the row
must not add a request of its own, and it must follow that state when
Start/Stop or a live refresh changes it.

The design doc is already updated — see the
`> **Updated (docs/prompts/0012):**` blockquote in
`docs/fase5-cockpit/ontwerp-v0.1.md`. Nothing further to write there.

## Verify live

Open `project.html` on a project with a running watcher: the button is
visible without scrolling and produces a fresh poll line in the watcher
log. Stop the watcher — the button goes disabled without a page reload.

## Not in scope

The same button on the other pages, a sticky action row, and moving any
other watcher control.
