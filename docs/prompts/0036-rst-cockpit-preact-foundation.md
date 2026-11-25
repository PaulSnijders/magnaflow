---
title: "Cockpit on Preact + htm: foundation, project page, front door"
cmd: 0036-cmd-cockpit-preact-foundation.md
done: 2026-11-25
summary: "The front door and the project page now render with a vendored Preact + htm (one ES module per page, no build step), so open lists, logs, focus and typed input survive live updates; nothing else should look or behave differently. After the self-update, do the human checks from the cmd: one project page while a command runs, Add project via Browse, and a clean console on both pages."
---

# Result

## What was done

- **Vendor**: `wwwroot/assets/vendor/preact-htm.js` holds `htm@3.1.1`'s
  `preact/standalone.module.js`. It came from the npm registry tarball
  (`registry.npmjs.org/htm/-/htm-3.1.1.tgz`) and is byte-identical to the
  unpkg copy (checked with `cmp`). The only addition is a first comment line
  giving the package, version and source URL; everything after line 1 is
  byte-for-byte upstream (checked). `vendor/LICENSE` carries both license
  texts verbatim from the npm packages.
- **`assets/ui.js`** (ES module, `// @ts-check`) re-exports `html`, `render`
  and the hooks, and holds these components:
  - `StatusPill`, `LiveBadge`, `Breadcrumb`, `LogView`, `SummaryBar`,
    `Markdown`, `Frontmatter`, all from the task.
  - Two small additions both pages needed: `SoundToggle` adopts the DOM
    button `Cockpit.soundToggle()` builds, and `Dialog` is a native
    `<dialog>` with `showModal()`/`close()` through a ref, plus `onClose`.
  - `ctrlEnter(submit)` replaces `Cockpit.submitOnCtrlEnter` in templates.
  - `Markdown` is the only `dangerouslySetInnerHTML`. It re-runs
    `Cockpit.rewriteLaneLinks` after each render when given `project`.
  - The two converted pages use neither `Markdown`, `Frontmatter` nor
    `LogView`; they are there for 0036B.
- **`assets/types.js`**: JSDoc `@typedef`s for every response the two pages
  read, plus `LiveState` (the `onState` payload). The pages reference them
  with `import("../types.js")` typedefs.
- **`assets/pages/project.js`** and **`assets/pages/index.js`** follow the
  page pattern:
  - One `state` object, and loaders with the same endpoints and triggers
    that assign into it and call `rerender()`.
  - UI state is `useState` in the owning card: lane expanded, watch log
    "load all", each run service's and each action's open log, every button
    in flight, the branch selection, the commit message, the draft
    inputs, and the whole Add project dialog including the browse picker.
  - The Switch, Pull/Sync, Commit all, Promote and action Run disabled
    state, label and `title` are computed in render.
    `updateSwitchControls`, `updatePullControl`, `rerenderStable`,
    `expandedRunLogs`, `actionLogsOpen`, `laneExpanded` and
    `watchLogExpanded` are gone.
- **HTML**: `project.html` and `index.html` keep `<head>` and the header,
  then `<div id="app">`, then `app.js` as a classic script, then the page
  module. Neither page used `md.js`, so neither loads it.
- **`app.js`**: one appended line, `window.Cockpit = Cockpit;`.
  `Cockpit.live()` is untouched. The converted pages pass no `badge` and
  render `LiveBadge` from `onState`.
- **README**: the opening line now names the vendored Preact + htm and one
  ES module per page, with still no SPA and no build step.
- **Specs**: `docs/specs/STATUS.md` has a "Spec ahead" line:
  `#page-rendering` holds for these two pages, the other five wait for
  0036B. It also has a "Recent spec updates" line. `cockpit/index.md` and
  `cockpit/project.md` needed no edit, since behaviour is unchanged.
  `spec_lint`: 0 problems.

## Verification

- `dotnet test MfCockpit.slnx`: 429 passed, 0 failed. This includes the
  3 new `StaticFilesCacheTests` rows and the new `WwwrootReferencesTests`:
  - local `src`/`href` in every page, including the logo and favicon
    files the csproj links in from the repo's `assets/`;
  - relative static, bare and JSDoc `import(...)` specifiers under
    `assets/`;
  - no `innerHTML`/`escapeHtml` in the four files.

  The import test also asserts that it found the three imports the pages
  are built on, so a broken pattern fails rather than passing silently.
  As a mutation check, I renamed the vendor import in `ui.js` to
  `preact-htmx.js` and the test failed; then I reverted it.
- **`node --check` ran** (node v26.12.0) on `ui.js`, `types.js`,
  `pages/project.js` and `pages/index.js`. All four passed.
- **Headless Chromium** (CDP, scratch cockpit on port 5299 with this repo
  registered and a temp `new_project.root`):
  - Both pages: no console errors or exceptions. The only console output
    was the autoplay warning `AudioContext was not allowed to start`, the
    expected pending state of the sound toggle.
  - Project page:
    - Tab title `🟠 running · magnaflow`; breadcrumb, summary bar,
      `--summary-bar-h` and the live badge render.
    - I expanded the lane (38 rows), clicked the watch log's "load all",
      opened an action log and focused the branch select, then forced
      `refetchAll` through `visibilitychange`. All four stayed as they
      were.
    - On the dirty tree, Switch and Pull are disabled with the same
      "working tree is dirty…" titles as before. Commit all is enabled
      with "Commit everything and push (Ctrl+Enter)".
  - Front door:
    - Opening the dialog and Browse… from a hint path works. Entering a
      folder, then "Use this folder", fills Path and an empty Name, closes
      the panel and focuses Path.
    - Cancel closes, and reopening starts empty on Existing.
    - Validation reads "name and path are required". The New tab preview
      reads `/tmp/mfc-test/root/My-New-Proj`.
    - A real add against the scratch config showed "Added … with
      warnings:" and the button changed to "Open project".
  - `specs.html` (unconverted) still loads with its summary bar and a
    clean console.
- **Not exercised in a browser**: the Run card. This repo has no `run:`
  services (`configured: false`), and I did not fake mf-run. Its markup
  and handlers are a direct port, so check it on a project with services
  and a stable instance.

## Decisions taken while implementing

- **`window.Cockpit = Cockpit` added to `app.js`.** A top-level `const` in a
  classic script is a global binding but not a `window` property. The
  first browser run failed on exactly this (`Cannot read properties of
  undefined (reading 'qs')`). `renderMarkdown` is a function declaration,
  so it is on `window` already.
- **Header slots get their own small `render` roots.**
  - Project page: `Breadcrumb` renders into `#breadcrumb` once.
  - Index: `SoundToggle` and `LiveBadge` render into `<nav id="header-nav">`.
    That `<nav>` used to hold a static `#live` span with the toggle
    inserted before it; the result is the same DOM.

  The vendored standalone build has no `createPortal`, and the header stays
  static HTML as the page pattern asks. The summary bar sits inside the
  `#app` mount, before `<main>`. It is still sticky, because no CSS
  selector depends on it being a child of `body`. The project page's
  unused header `#live` span stays as it was.
- **`LiveBadge` formats the "· HH:MM" suffix itself.** `onState` gives
  `lastUpdated`, not the suffixed text. That is a 3-line copy of `live()`'s
  `clock()` rather than a change to `live()`.
- **The dialog remounts on every open.** The page bumps a `key`, which
  replaces the old `openAddProjectDialog` reset with no reset code.
- **Small differences that fall out of the model**, none of them described
  in the specs:
  - A run service's result line (`start: exit 0 — …`) now stays after the
    refetch that follows. Before, the re-render wiped it at once.
  - A failed Start all / Stop all now stays in the card's error line.
    Before, the `loadRun()` right after it overwrote it with
    `status.error || ""`.
  - Index Branch and Run cells keep their last value during a refetch.
    Before, they flashed back to `…`.
  - An open run-service log refetches when the run status refetches, as
    before. A lane refetch no longer re-renders the stable row's log, since
    Promote's disabled state is derived in render.
- **The lane's "Make ready" label is kept.** `cockpit/project.md` calls it
  **Ready**; the code said "Make ready". This refactor keeps the code. The
  spec wording is a pre-existing mismatch left for a spec-drift pass.

## Uncertain / for the next session

- I did not check the Run card and stable row in a browser (see above).
- A human should still look at both pages after the self-update; the
  checks are in the cmd. The cockpit changes reach the live worker by
  self-update.
- `.gz` files appear next to the assets in the build output. That is the
  Web SDK's static-asset compression, not something this cmd added.

## Self-answered questions

- **"Add `vendor/LICENSE` with the MIT texts of Preact and htm"**, but htm
  is **Apache-2.0**, not MIT. Its `package.json` (`"license":
  "Apache-2.0"`) and its `LICENSE` say so. I settled on Preact's MIT text
  plus htm's Apache-2.0 text, both verbatim from the npm tarballs, under a
  two-line header naming each. Decision 0022 also says "its MIT license";
  it is a record, so I did not edit it. Someone may want to correct that
  wording.
- **How do modules read `window.Cockpit` when `app.js` declares it with
  `const`?** One export line in `app.js` (see Decisions). The cmd keeps
  `app.js` classic, and this keeps it so.
- **Where do the header's breadcrumb, live badge and sound toggle render,
  given "the header plus one mount"?** Into their existing header elements
  through separate `render` roots. The summary bar is inside the mount.
- **Do the converted pages load `md.js`?** No. Neither page renders
  markdown. `Markdown` reads `window.renderMarkdown` when 0036B's pages
  use it.
- **Which helpers move into `ui.js`?** The HTML-building ones become
  components, as the cmd lists. The non-HTML helpers stay in `app.js` and
  are read from `window.Cockpit` (`api`, `qs`, `formatDate`, `displayUrl`,
  `projectTitle`, `cueTracker`, `getOverview`, `wireNav`,
  `rewriteLaneLinks`, `splitFrontmatter`, `soundToggle`), so nothing is
  duplicated before 0036B.
