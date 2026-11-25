---
title: "Cockpit on Preact + htm: the remaining five pages"
status: ready
created: 2026-11-25
---

## Context

`0036` vendored Preact + htm, added `assets/ui.js` and `assets/types.js`,
and converted `project.html` and `index.html` with one page pattern
(Task 4 of `0036-cmd-cockpit-preact-foundation.md`; its rst says what
the implementation settled). This cmd converts the other five pages
the same way, then makes `app.js` and `md.js` ES modules and removes
the HTML-string helpers.

Read first: `0036`'s cmd and rst, `docs/decisions/0022-cockpit-preact-htm.md`,
`docs/specs/cockpit/index.md#page-rendering`, and the page specs
`cockpit/chat.md`, `command.md`, `config.md`, `scratchpad.md` and
`specs.md` in full. They describe behaviour that must stay the same.

All work is in `tools/mf-cockpit/`. This is a refactor: no visual
redesign, no API change.

## Task

1. **Convert**, each to `assets/pages/<page>.js` with the `0036`
   pattern:
   - `specs.html`: spec tree, rendered spec through `Markdown`, the
     cross-link rewriting into the page (`rewriteLaneLinks`'s job, now
     done on the rendered container after render or inside `Markdown`).
   - `command.html`: frontmatter, rst/qa/pln markdown, evidence logs,
     follow-up.
   - `chat.html`: the streamed reply grows in place. Autoscroll to the
     bottom only while the user is at the bottom. The "running" banner
     stays on `lane` events, after each reply and every 15 s.
   - `config.html`: the editor stays the dirty-guarded source of truth.
     A refetch never overwrites unsaved text, and the conflict note
     with Reload/Overwrite stays.
   - `scratchpad.html`: note (hash/409 contract, dirty flag) and the
     Files card (XHR upload progress rows, drop highlight, list).
2. **Modules**: turn `app.js` and `md.js` into ES modules with named
   exports. Every page imports them; no `window.Cockpit`/global
   `renderMarkdown` remains, and no classic `<script src>` is left.
   Keep `Cockpit.live()`'s behaviour exactly.
3. **Remove** helpers that only produced HTML strings and are now
   unused (`escapeHtml`, `statusPill`, `renderBreadcrumb`,
   `renderLogLines`, `frontmatterHtml`, …). Keep the pure ones
   (`qs`, `api`, `formatDate`, `projectTitle`, `displayUrl`,
   `splitFrontmatter`, …).
4. Extend `assets/types.js` with the responses these pages read.

## Must not change

- Any API endpoint, SSE kind or server code beyond tests.
- Behaviour in the five page specs: link vetting and markdown rules
  (`chat.md#link-safety`, `#markdown-rendering`), the config conflict
  flow, the scratchpad caps and contracts, and chat's read-only nature.
- `md.js`'s rendering output for the same input.
- `style.css` class names and look.
- No npm, no `package.json`, no build step, no CDN reference.

## Verify

- `WwwrootReferencesTests`: widen the no-`innerHTML`/no-`escapeHtml`
  assertion to every `.html` and every `.js` under `wwwroot/assets/`.
  Exempt `vendor/` and `md.js` (it builds HTML on purpose). Also assert
  that no `.html` contains a classic `<script src>` or an inline
  `<script>` without `type="module"`.
- `StaticFilesCacheTests`: add the five `assets/pages/*.js`.
- If `node` (22+) is on PATH: `node --check` on every module under
  `wwwroot/assets/` (excluding `vendor/`). Say in the rst whether it ran.
- All existing cockpit tests stay green.
- **Human, after the self-update / `install.ps1`**: walk every page:
  - **Specs**: a cross-link stays inside the page.
  - **Command**: logs expand and survive a live event.
  - **Chat**: a streamed reply; scroll up mid-stream and it does not
    yank you down.
  - **Config**: type, wait for an `evidence` event, and the text is
    kept; the conflict note works.
  - **Scratchpad**: note save, and an upload with progress.
  - The browser console is clean everywhere.

## Not in scope

- Visual changes or new features: a follow-up once this lands.
- Merging pages into an SPA: ruled out in decision 0022.
