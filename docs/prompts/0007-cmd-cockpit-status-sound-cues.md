---
title: "mf-cockpit: play a bird chirp when a command reaches done, questions or aborted"
status: done
attempts: 1
created: 2026-08-11
---

## Context

The dashboard refreshes itself (v0.5 item 1) but says nothing. With the
page open in a background tab there is no way to notice that a command
finished, failed, or started asking questions, short of looking at it.

Design: `docs/fase5-cockpit/ontwerp-sound-cues.md`. Client-side only —
no endpoint, no server change, no new write action.

## Task

1. **Chirp synthesis** in `assets/app.js`: `Cockpit.chirp(kind)` for
   `done` (rising two-note whistle), `questions` (short repeated chirp,
   rising at the end) and `aborted` (descending two-note call, lower).
   WebAudio only — a sine oscillator per note with a fast frequency
   glide, gain envelope of ~120–200 ms with an attack under 10 ms, peak
   gain ~0.15. No sample files, no new dependency.

2. **Autoplay handling**: create the `AudioContext` lazily and
   `resume()` it on the first click or keypress on the page. While it is
   still suspended, the toggle renders a *pending* state whose `title`
   says a click is needed — never "on" while silently swallowing cues.

3. **Transition detection**, a pure function over a previous
   `project + command id → status` map and the freshly fetched items:
   returns at most **one** cue, priority `aborted > questions > done`.

   - The first render after page load fills the map and returns nothing.
   - An id seen for the first time mid-session is recorded silently and
     can only chime on its *next* change.
   - Only `done`, `questions`, `aborted` produce a cue; the map still
     records every status so a later transition is detected correctly.

   Wire it into the existing `Cockpit.live` refetch on `index.html`
   (over each project summary's `Latest`) and `project.html` (over the
   full lane). No other page.

4. **Toggle**: a 🔔/🔕 button in `Cockpit.summaryBar` and in the index
   header, state in `localStorage` under one key, default on. Off means
   no `AudioContext` work at all.

## Verify live

Flip a cmd to each of the three statuses and hear a distinct call;
reload a page holding finished commands and hear silence; make several
commands terminal in one refetch and hear exactly one sound; toggle off
and confirm silence across a reload.

Not in scope: a volume slider, per-status toggles, a cue for `running`,
recorded sample files, the Notification API, replaying missed cues after
a reload.
