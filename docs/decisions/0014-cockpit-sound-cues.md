---
date: 2026-08-11
topic: cockpit
status: accepted
---

# 0014 — mf-cockpit — design: status sound cues

> Trimmed to the four-section format on 2026-10-08; design detail that the specs now hold was removed. The full original is in git history.

## Situation

After `0013-cockpit-quality-of-life.md` the dashboard stays current on
its own, but you still have to look at it to notice that a command
needs you. The wish: a short bird-like chirp when a command hands work
back to the human. Read-only and client-side; frontend rules unchanged.

## Options

WebAudio synthesis or recorded `.ogg` samples; a per-browser or a
per-project toggle; terminal statuses only or also `running`; every
summary-bar page or only pages that already fetch lane data.

## Measurement

No binary assets or network fetches, no config write for a
preference, no sound for what the human just caused, no extra fetch
just to make noise, degraded states shown honestly.

## Choice

**Terminal statuses only.** `draft`, `ready` and `running` are
transitions the human just caused by clicking; announcing them is
noise. `running` stays debatable: worth revisiting once mf-watch picks
up work Paul did not queue himself.

**Only observed transitions.** The first render is a silent baseline,
held in memory per page load and keyed by project + command id, so
opening onto twelve finished commands is quiet and nothing is persisted.
At most one sound per refetch, by priority: after laptop wake or a
polling stretch, a batch of news is one nudge, not a chorus; the page
shows the full picture.

**Index and project page only.** They already refetch the lane data;
the other summary-bar pages show the toggle but stay silent.

**Synthesis over samples.** No binary assets in git, no sample
licensing, nothing to 404. It sounds like a whistle, not a recording;
fine, since the job is telling the three outcomes apart.

**Per-browser toggle, default on.** "Sound on" belongs to where the
human sits, not to the project: the desk laptop loud, the meeting-room
one silent, without a config write. Browser autoplay rules keep a fresh
browser quiet until the first click, and the toggle shows that pending state
instead of looking enabled while swallowing cues. Background tabs play
too; that is the useful case.

**No JS test runner.** The diff is a pure function, verified live, as
with 0013's host rewrite.

Not in scope: a volume slider, per-status enable, real samples, the
Notification API or OS notifications, sound on pages that do not
already fetch lane data, replaying cues missed before a reload.

Current behavior: `specs/cockpit/index.md#sound-cues`,
`specs/cockpit/project.md#lane`.
