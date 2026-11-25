---
title: "Cockpit on Preact + htm: the remaining five pages"
cmd: 0036B-cmd-cockpit-preact-remaining-pages.md
done: 2026-11-25
summary: "All seven cockpit pages now render with Preact + htm as ES modules (no window.Cockpit, no innerHTML outside md.js); nothing should look different, except that chat stops yanking you down when you scroll up and scratchpad 'keep mine' really saves. After the self-update, walk the human checks in the cmd."
---

# Result

## What was done

- **Five pages converted** with the 0036 pattern. Each HTML file is now
  `<head>`, the static header, `<div id="app">` and one
  `<script type="module">`:
  - `assets/pages/specs.js`
  - `assets/pages/command.js`
  - `assets/pages/chat.js`
  - `assets/pages/config.js`
  - `assets/pages/scratchpad.js`
- **`app.js`** is an ES module with named exports: `qs`, `api`,
  `formatDate`, `statusIcon`, `projectTitle`, `displayUrl`, `live`,
  `getOverview`, `wireNav`, `rewriteLaneLinks`, `splitFrontmatter`,
  `chirp`, `cueTracker`, `detectCue` and `soundToggle`.
  - The IIFE and `window.Cockpit` are gone.
  - Pages import it as `import * as Cockpit from "../app.js"` (a
    namespace import), so `project.js`/`index.js` call sites were not
    touched; only their import lines changed.
  - `index.html` and `project.html` lost their classic `app.js` tag.
- **`md.js`**: `renderMarkdown` gained `export`. Nothing else in the
  renderer changed (one comment line added), so its output is identical.
  `ui.js`'s `Markdown` imports it.
- **Removed from `app.js`**: `escapeHtml`, `statusPill`,
  `renderBreadcrumb`, `summarizeJson`, `renderLogLines`, `logView`,
  `frontmatterHtml`, `summaryBar` and `submitOnCtrlEnter`.
- **`live()`** lost only its `badge` option, along with that option's
  `innerHTML` and label patching. No page passes it any more: every
  marker is `LiveBadge` fed by `onState`. SSE, heartbeat, backoff,
  polling, guard, `onGuardedSkip`, in-flight dedupe and the `onState`
  payload are unchanged.
- **`ui.js`**:
  - It imports `app.js`/`md.js` and no longer re-exports a `Cockpit`
    object.
  - New `startProjectChrome({project, label, chrome, rerender})`:
    header breadcrumb, `wireNav`, the summary bar's data from the one
    `/api/overview`, and a `refreshSummary` for lane/watch events. It
    replaces what `Cockpit.summaryBar()` did for specs, command, config
    and scratchpad.
  - `LogView` keys its lines by content plus occurrence (see Decisions).
- **`types.js`**: new typedefs `LaneFile`, `CommandDetail`,
  `CreatedCommand`, `SpecsEntry`, `SpecsResult`, `CockpitConfig`,
  `ConfigSummary`, `ProjectConfig`, `ConfigSaveResult`, `Scratchpad`,
  `ScratchpadSaveResult`, `ScratchFile` and `ChatFrame`; `LogTail` gained
  `truncated`. The shapes come from `Models/Dtos.cs`,
  `SpecsEndpoints.cs` and `ChatEndpoints.cs`.
- **Tests**:
  - `WwwrootReferencesTests`:
    - The no-`innerHTML`/`escapeHtml` theory runs over every `.html` and
      every `.js` under `assets/`, except `vendor/` and `md.js`.
    - A fact asserts that this list really contains all seven pages and
      modules, plus `app.js` and `ui.js`, and excludes the two exempt
      files.
    - New theory: every `<script>` in every page has `type="module"`,
      which covers both classic `src` scripts and inline ones.
    - The import test also asserts `ui.js -> ./app.js`, `ui.js -> ./md.js`
      and `pages/<page>.js -> ../app.js` for all seven pages.
  - `StaticFilesCacheTests`: rows for the five new page modules.
- **Specs**:
  - `cockpit/chat.md`, Stream contract: the preview grows in place, and
    the log follows it only while you are at the bottom.
  - `STATUS.md`: the 0036 "Spec ahead" line is removed (now `none`), and
    a "Recent spec updates" line is added. `spec_lint`: 0 problems.

## Verification

- `dotnet test MfCockpit.slnx`: 455 passed, 0 failed.
- **`node --check` ran** (node v26.12.0) on every module under
  `wwwroot/assets/` except `vendor/`: `app.js`, `md.js`, `types.js`,
  `ui.js` and the seven `pages/*.js`. All passed. A small throwaway
  script also checked that every named import from `ui.js`/`app.js`, and
  every `Cockpit.x` used, is a real export.
- **Headless Chromium**, driven over CDP against a scratch cockpit on port
  5299. It had a temp config with chat pointed at a fake streaming
  script, a scratch git project in `/tmp`, and this repo registered
  read-only.
  - **All seven pages**: they render, the live badge goes to "live", and
    the console shows no exceptions or errors. The only output was the
    expected `AudioContext was not allowed to start` warning, plus the
    two 409s I provoked on purpose.
  - **Specs**:
    - The tree, the `docs/specs / …` crumbs and the header breadcrumb
      render.
    - A cross-link `../concepts/design.md#why` became
      `specs.html?…&path=concepts%2Fdesign.md#why` with no `target`.
      Clicking it stayed in the page.
    - A link that climbs out of `docs/specs/` renders as plain text.
      `<b>` in the markdown is escaped.
    - A `.yml` file shows dimmed `#` comments and keeps the quoted `#`.
  - **Command**:
    - The title gets its status icon; heading, pill, meta, summary,
      frontmatter block and markdown render.
    - A `./0001-rst-first.md` link became `command.html?…&id=0001-first`.
    - The follow-up card shows for `done`. Typed feedback survived a
      forced refetch.
    - On 0036's real claude.log, I opened a JSON line and forced a
      refetch: the same node stayed open. Raw/Pretty toggles both ways.
  - **Chat** (fake 60-chunk stream):
    - The reply grows in place. At the bottom the log follows it. After
      scrolling to the top mid-stream it stayed at 0 while the content
      kept growing. Scrolled back down, it followed again.
    - The final reply renders as markdown. `javascript:` renders as text
      and the `https:` link gets `target=_blank rel=noopener`, through
      the unchanged `md.js`.
    - Send and Make this a command are disabled while streaming and
      re-enabled after.
  - **Config**:
    - I typed, then appended to the scratch project's
      `.magnaflow/config.yml` to fire `evidence`. The note appeared, the
      text and focus were kept, and Save gave the 409 message.
    - Overwrite saved my text over the disk version and hid the note.
      Reload took the disk version.
    - A clean editor follows the disk on the next event. The cockpit and
      summary cards render.
  - **Scratchpad**:
    - Status went "· empty" → "· unsaved" → "· saved HH:MM" after the
      1.5 s autosave.
    - A server-side change, then Ctrl+S with "keep mine", wrote mine
      (see Decisions).
    - A synthetic dragenter set `drag-over` and the drop cleared it.
      A 3 MB upload showed `· 0%` and `· 100%` rows, which went away on
      success.
    - The list shows the sanitised name, the size and the time, with
      Download and Delete; Delete removed the file.
- **Not exercised**:
  - A real `claude` behind chat: I used a fake script with the same
    stream-json shape.
  - A command in `draft`, so the Make ready button was not clicked in
    the browser. It is a direct port.
  - Upload progress over a slow link: on localhost the rows jump from 0%
    to 100%.

## Decisions taken while implementing

- **`live()`'s `badge` option was removed rather than rewritten.** After
  this cmd nothing passed it, and its `innerHTML` would fail the widened
  test. Everything else in `live()` is byte-for-byte as it was.
- **Chat's header marker** is a `<span id="live" style="display:contents">`
  slot, with `LiveBadge` rendered into it through its own root. The
  resulting DOM is the old `span.badge-live`. Chat has no summary bar,
  as before, and does not call `startProjectChrome`, so it still makes no
  `/api/overview` request (chat.html has no `#chat-link`).
- **The editor texts are page state, not `useState`** (config, scratchpad).
  The live guard, Save, Overwrite, Reload, autosave, blur,
  `visibilitychange` and the 409 handler all live outside the component
  and read or reset the text. The textareas are controlled
  (`value=${state.text}`), and every keystroke updates `state.text`.
- **The chat input is uncontrolled** (a ref, read on submit). With a
  controlled value, an input event and a Ctrl+Enter in the same tick
  submitted the stale (empty) state. A user can't hit that, but the
  browser test did. The DOM keeps the typed text across re-renders.
- **A rendered chat reply is itself the bubble.** `Markdown` carries
  `class="chat-msg agent"`, so `.chat-msg.agent > *:first-child` still
  reaches the markdown's own first block, and the look is unchanged.
  Consequence: in the rare `timedOut` *with* `finalText` case, the
  `[timed out]` note sits just under the bubble instead of inside it.
  With no `finalText`, it is inside, as before.
- **`LogView` keys lines by content + occurrence.** Unkeyed, an open JSON
  line in a bounded tail that shifted under a live event would leave the
  open state at its old index, on a different line.
- **Scratchpad "keep mine" now saves at once.** In the old page, the
  nested `save(true)` inside `handleConflict` ran while the outer
  save's `saving` flag was still set, so it returned at once. The status
  stuck at "saving…" and nothing was written until the next
  edit/blur/hide. `cockpit/scratchpad.md` says keep-mine "re-reads the
  server hash and overwrites", so I release the flag before the nested
  save. I count this as fixing code to match its spec, not a behaviour
  change against the spec.
- **Ready errors still use `alert()`, and chat still lets Ctrl+Enter fire
  a second send while one streams**, as the old page did. I didn't fix
  those in a refactor.
- **Loading placeholders**: before the first load, the evidence log boxes
  stay empty rather than showing "(empty)", as before.

## Uncertain / for the next session

- The human walk-through in the cmd is still needed after the
  self-update. In particular: a real chat reply, and a command whose log
  grows while you have a JSON line open.
- `project.js`/`index.js` were only re-tested for "renders, clean
  console, title". Their logic did not change; only their imports did.
- The header `#live` span on specs, command, config, scratchpad and
  project stays empty, as it did since the summary bar took the marker.
  It could be dropped from those headers in a visual follow-up.

## Self-answered questions

- **Where does the cross-link rewriting go on specs?** Into md.js's own
  `rewriteLink` option, passed through `Markdown`'s `opts`, as one
  object per page load so the memo holds. `rewriteLaneLinks` is for lane
  files; on specs it was never used. Command keeps it through
  `Markdown`'s `project` prop.
- **How do pages import `app.js`: named imports or a namespace?** A
  namespace import of the named exports (`import * as Cockpit`). That is
  still ES-module named exports with nothing global, and it kept the
  0036 pages' call sites as they were.
- **Remove `submitOnCtrlEnter` and `summaryBar` too?** Yes. Neither is in
  the cmd's list, but both are unused once every page is converted, and
  `summaryBar` built its markup with `innerHTML`.
- **What happens to `live()`'s `badge`?** Removed (see Decisions).
- **Unsaved editor text in `useState` or page state?** Page state, for
  config and scratchpad (see Decisions).
- **"Autoscroll only while at the bottom" is a behaviour change. Does a
  spec move?** Yes: one sentence in `cockpit/chat.md`.
- **Shared chrome for four pages.** Added one helper to `ui.js`
  (`startProjectChrome`) rather than copying the same 15 lines four
  times.
