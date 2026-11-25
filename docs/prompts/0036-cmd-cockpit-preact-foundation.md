---
title: "Cockpit on Preact + htm: foundation, project page, front door"
status: done
created: 2026-11-25
attempts: 1
---

## Context

The cockpit pages build HTML as template strings in `innerHTML`.
`project.html` carries its re-render state by hand: six page flags,
`updateSwitchControls`/`updatePullControl`, `laneExpanded`,
`watchLogExpanded` and per-action log state. Every page escapes file
and model text by hand with `escapeHtml`. The whole frontend moves to
Preact + htm, vendored, in two cmds. This one lays the foundation and
converts `project.html` (the heaviest page) and `index.html`.
`0036B` converts the other five and removes the old helpers.

Read first; they are the contract:
- `docs/decisions/0022-cockpit-preact-htm.md`: why, and what is ruled out.
- `docs/specs/concepts/design.md`, the "Cockpit appearance" bullet.
- `docs/specs/cockpit/index.md#page-rendering` (new, spec ahead) and
  `#serving`.
- `docs/specs/cockpit/project.md` and `docs/specs/cockpit/index.md` in
  full. Everything they describe must behave the same afterwards.

`0035` (Add project browse) runs before this one in queue order and
changes `index.html`. Convert its path picker too.

All work is in `tools/mf-cockpit/`. This is a refactor: no visual
redesign, no API change.

## Task

1. **Vendor** `htm@3.1.1`'s `preact/standalone.module.js` (Preact 10 +
   hooks + htm in one ESM file) to `wwwroot/assets/vendor/preact-htm.js`.
   Fetch it from the npm registry tarball or unpkg at that exact
   version, byte-for-byte. Put a one-line comment on top with the
   package, version and source URL. Add `wwwroot/assets/vendor/LICENSE`
   with the MIT texts of Preact and htm. If neither source is reachable
   from the run, stop with a question; do not hand-write or approximate
   the file.
2. **Shared components**: new `wwwroot/assets/ui.js`, an ES module
   importing `html`, `render` and the hooks from `./vendor/preact-htm.js`.
   Port what the two pages need from `Cockpit`'s HTML-string helpers as
   components: `StatusPill`, `LiveBadge`, `Breadcrumb`, `LogView`,
   `SummaryBar`, `Markdown` and `Frontmatter`. `Markdown` is the one
   place that injects a string (`dangerouslySetInnerHTML` with
   `renderMarkdown` output). Start every module with `// @ts-check`.
   Put JSDoc `@typedef`s for the API responses the converted pages read
   in `wwwroot/assets/types.js`, and reference them from the pages.
3. **Keep `app.js` and `md.js` classic scripts** for now. The five
   unconverted pages still use them. Converted pages load them with a
   plain `<script src>` *before* their module, so the module reads
   `window.Cockpit`/`renderMarkdown`. Leave `Cockpit.live()` (SSE,
   heartbeat, backoff, polling fallback, guard) as it is. Only its
   badge output may move to `LiveBadge` through `onState`. `0036B`
   turns both files into ES modules.
4. **Page pattern** (use it on both pages; `0036B` copies it):
   - The HTML file keeps `<head>`, the header and one mount element.
     The page script moves to `wwwroot/assets/pages/<page>.js`, loaded
     with `<script type="module">`.
   - Server data lives in one plain page-state object. Loaders keep
     their current endpoints and triggers. They assign into that object
     and call `rerender()`, which is
     `render(html\`<Page state=${state}/>\`, mount)`. Preact diffs, so
     the refetch-and-re-render flow stays as it is.
   - Pure UI state (expanded, toggles, unsaved input, which dialog is
     open) is `useState` inside the component that owns it, so it
     survives a refetch without being carried.
   - Derived controls are computed in render, not patched: the Switch
     and Pull/Sync disabled state, label and `title`, and Commit all.
     `updateSwitchControls`/`updatePullControl` disappear.
   - `<dialog>`s stay native (`showModal()` via a ref).
5. **Convert `project.html`**: lane, git, branches, run, watcher, watch
   log and actions cards.
6. **Convert `index.html`**: attention list, projects table and the Add
   project dialog with its tabs, preview, warnings, scaffold output and
   the `0035` browse picker. Keep the tab-title logic
   (`Cockpit.projectTitle`) and the sound cues working.
7. **README**: in `tools/mf-cockpit/README.md`, replace "no SPA
   framework" in the opening line with one line naming the vendored
   Preact + htm, ES modules per page, still no SPA and no build step.

## Must not change

- Any API endpoint, SSE kind or server code beyond tests.
- Behaviour described in `cockpit/project.md` and `cockpit/index.md`:
  labels, refusals, `title` texts, confirm prompts, sound cues, tab
  titles, link targets and the `displayUrl` host rewrite.
- `style.css` class names and look; add only what a component needs.
- The other five pages: they keep working unchanged on the classic
  `app.js`/`md.js`.
- No npm, no `package.json`, no build step, no CDN reference.

## Verify

- `StaticFilesCacheTests`: add `/assets/vendor/preact-htm.js`,
  `/assets/ui.js` and `/assets/pages/project.js` to the theory data.
- New xUnit `WwwrootReferencesTests`, reading the files from the
  source `wwwroot/`. A typo in an import path gives a blank page and
  no error anywhere else, so this test catches it:
  - every `src=`/`href=` to a local asset in each `.html` resolves to a
    file;
  - every relative `import … from "./…"`/`"../…"` in each `.js` under
    `wwwroot/assets/` resolves to a file;
  - `project.html`, `index.html`, `assets/pages/project.js` and
    `assets/pages/index.js` contain no `innerHTML` and no `escapeHtml`.
- If `node` (22+) is on PATH, run `node --check` on each new module and
  say in the rst whether it ran. This is a check, not a build step;
  nothing is added to `.magnaflow/config.yml`.
- All existing cockpit tests stay green.
- **Human, after the self-update / `install.ps1`**:
  - Open a project page while a command runs. Expand the lane, open the
    watch log and an action log, focus the branch select. They stay as
    they were through several live events.
  - The Switch, Pull/Sync and Commit all buttons grey out with the same
    `title` on a dirty tree.
  - On the front door, add a project via browse.
  - Check the browser console is clean on both pages.

## Not in scope

- The other five pages, and turning `app.js`/`md.js` into modules: `0036B`.
- Visual changes or new features: a follow-up once all pages share the model.
- A CSS library: ruled out in decision 0022.
