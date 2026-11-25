---
date: 2026-11-25
topic: cockpit, frontend, dependency
status: accepted
---

# 0022 — Cockpit pages render with Preact + htm, vendored

## Situation

The cockpit's seven pages build their HTML as template strings assigned
to `innerHTML`, with a thin `Cockpit` helper in `assets/app.js`.
Decision 0013 set a threshold for moving one page to Preact+htm:
"hand-carrying re-render state for a third time on one page".
`project.html` is past it. Six page-level flags (`gitDirty`,
`laneRunning`, `watcherAlive`, …) are shared across four loaders.
`updateSwitchControls`/`updatePullControl` patch buttons by hand, and
`laneExpanded`, `watchLogExpanded` and the per-action logs are carried
through every re-render. Every page also has to remember `escapeHtml`
on text that comes from repo files and model output. The product owner
wants the whole frontend on one model, not one page.

## Options

- **Stay hand-written.** No dependency. The state-carrying and
  escape-by-hand cost grows with every card.
- **Alpine.js.** Binding attributes in the HTML. The logic moves into
  attribute strings, and every template is rewritten into a different
  shape.
- **Preact + htm, vendored.** One ESM file (`htm/preact/standalone`,
  Preact 10 + hooks + htm) checked into `wwwroot/assets/vendor/`.
  htm's `${…}` templates are nearly the current template strings, so
  the conversion is close to mechanical.
- **Vue/React/Lit with npm.** Needs a build step and a node toolchain
  on every machine that builds the cockpit.
- **A CSS library (Pico, Water) next to it.** It restyles every element
  and fights the theme in `style.css`, which already has its own
  tokens.

## Measurement

- 0007 holds: no build step and no CDN, because the cockpit must work
  on a LAN without internet. A vendored file served from `wwwroot/`
  meets that; npm does not.
- Escaping by default, and a DOM diff that keeps open/expanded state,
  focus and scroll across a live refetch, are the two gains that
  matter. Both come with Preact + htm.
- Future-proofing: one pinned file, swapped by hand to upgrade. Preact
  10's API has been stable for years. petite-vue, the other small
  candidate, is unmaintained.

## Choice

Preact + htm on every page, vendored as one file with its MIT license
beside it. Each page stays its own HTML file with its own module: no
router, no SPA. `md.js` stays the one markdown renderer, and its output
is the only HTML injected as a string. No CSS library: `style.css`
remains the design system.
