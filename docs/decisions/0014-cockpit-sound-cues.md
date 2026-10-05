---
date: 2026-08-11
topic: cockpit
status: accepted
---

# 0014 — mf-cockpit — design: status sound cues (2026-08-11)

> Migrated from `docs/decisions/0014-cockpit-sound-cues.md` on 2026-10-05. Pre-dates the four-section
> decision format; original text unchanged.

An addendum to `docs/decisions/0013-cockpit-quality-of-life.md`. Item 1 there made
every page refetch itself; this makes the dashboard *audible* as well as
current: when a command reaches a status that wants a human, the open
page plays a short bird-like chirp.

Read-only, client-side, no new write action — the invariant list in
`ontwerp-v0.1.md` is unchanged. Frontend rule unchanged: hand-written
JS in the thin `Cockpit` helper (`assets/app.js`), no build step, no
CDN, no new dependency.

## What makes a sound

Only three transitions, the ones that hand work back to the human:

| new status  | character of the call            |
| ----------- | -------------------------------- |
| `done`      | rising two-note whistle, bright  |
| `questions` | short repeated chirp, inquisitive, rising at the end |
| `aborted`   | descending two-note call, lower, darker |

`draft`, `ready` and `running` are silent: those are transitions the
human just caused by clicking, so announcing them is noise. (`running`
is the one debatable case — see "Not in scope".)

A sound fires only on an **observed transition**: the command's status
was already known to this page under some other value, and the new
value is one of the three. Consequences of that rule:

- **The first render after page load is baseline only, never sound.**
  Opening the dashboard onto twelve finished commands must be silent.
- A command id seen for the first time mid-session (a newly created
  lane item) records its status silently; it only chimes on its next
  change.
- Baseline is per page load, held in memory. Nothing is persisted, so
  a reload is a fresh silent start.

Keyed by `project + command id`, so the same command in two projects
cannot mask each other.

## Where it listens

- `index.html` — all projects, via the `Latest` field of each project
  summary (v0.5 item 6). So the index announces the newest command of
  every project reaching a terminal status.
- `project.html` — that project's full lane, so every command counts,
  not just the latest.

The other pages carrying the summary bar (`command.html`,
`config.html`, `specs.html`, `scratchpad.html`) show the toggle but
produce no sound of their own — they do not fetch lane data, and
duplicating a fetch just to make noise is not worth it.

No new endpoint, no server change: the diff runs over the JSON these
two pages already refetch through `Cockpit.live`.

## At most one sound per refetch

If a refetch reveals several transitions at once — the common case
after laptop wake, or after the polling fallback has been running
blind — exactly **one** sound plays, chosen by priority
`aborted > questions > done`. A batch of news is one notification, not
a chorus. The visual page already shows the full picture; the sound is
only the nudge to look at it.

## Autoplay: honest, not silent-broken

Browsers refuse to start audio before the user has interacted with the
page, so a freshly loaded dashboard cannot make a sound yet. The
`AudioContext` is created lazily and `resume()`d on the first click or
keypress anywhere on the page. Until that has happened the toggle shows
a distinct *pending* state with a title saying so ("klik ergens op de
pagina om geluid te activeren") — rather than appearing enabled while
swallowing every cue. Same reasoning as v0.5's honest live badge: a
degraded state is shown, not hidden.

## Synthesis, not sample files

Each call is generated with WebAudio: a sine oscillator with a fast
frequency glide per note, a short gain envelope (~120–200 ms per note,
attack under 10 ms), a light detuned second oscillator for body, and a
touch of vibrato so it reads as a bird rather than a UI beep. Peak gain
stays low (~0.15).

Why synthesis over `.ogg` files: no binary assets in git, no sample
licensing to track, no CDN, nothing to 404, and it works on a machine
that has never fetched anything. The cost is that it sounds like a
whistle, not like a recording — acceptable, since the job is "which of
the three happened", not birdsong fidelity.

## Toggle

A 🔔/🔕 button in the v0.5 summary bar (`Cockpit.summaryBar`), so it is
in the same place on every project page, plus in the index header.
State lives in `localStorage` under one key, **default on**; the
autoplay gesture requirement means a fresh browser still starts quiet
until the first click, which is a gentle enough introduction.

Per browser, deliberately: "sound on" is a property of where the human
is sitting, not of the project. The laptop on the desk can be loud
while the one in the meeting room is silent, without a config write and
without affecting anyone else who opens the dashboard.

Sounds play whether the tab is focused or in the background — the
background tab *is* the useful case.

## Testing

The diff is a pure function (previous status map + new items → the one
transition to announce, or none), so it is inspectable and exercised
live: flip a cmd to `questions`, to `aborted`, to `done`; reload with
finished commands present and confirm silence; open two tabs and
confirm both chime. Per v0.5 item 3's precedent we do not stand up a JS
test runner the repo does not otherwise have.

## Not in scope

A volume slider; per-status enable/disable; a sound for `running`
(worth revisiting once mf-watch picks up work Paul did not queue
himself — the observed need is the terminal statuses); real recorded
samples; the Notification API or desktop/OS notifications; sound on any
page that does not already fetch lane data; replaying missed cues after
a reload.
