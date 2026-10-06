---
title: "mf-cockpit: the specs page renders tables, anchors and non-markdown files properly"
cmd: 0022-cmd-cockpit-specs-rendering.md
done: 2026-10-06
summary: "The specs page now renders pipe tables, honours #anchors (including {#…} ones, which no longer show in headings), opens spec cross-links in the same tab and shows non-markdown files such as config.yml as readable YAML; md.js is plain text again. Nothing to do beyond the usual install.ps1 rollout."
---

# Report

All eight task items are done. `spec_lint` is green, and `dotnet test` for mf-cockpit passes
(311/311). The front end was verified live (see below).

## What changed

- **`wwwroot/assets/md.js`**
  - **NUL bytes:** the code-block placeholders are now the `"\u0000"` escape; the file is
    ASCII text (`file` agrees).
  - **Tables:** GitHub-style pipe tables, with `:---`/`---:`/`:---:` alignment rendered as
    `text-align`. Cells run through `inline()`, so code, emphasis and vetted links work.
    `\|` is a literal pipe (it is swapped for a `"\u0001"` placeholder before splitting).
    A table is recognised only when the header row and the delimiter row have the same
    number of cells. Short body rows are padded and long ones truncated to the header
    width. Output: `<div class="table-wrap md-table"><table>…`.
  - **Heading ids:** `MD_ANCHOR` and `mdGhSlug` are copies of `ANCHOR`/`ghSlug` in
    `scripts/spec_lint.mjs`, with a comment saying to change both together.
    - Every `{#…}` is removed from the visible text.
    - The explicit anchor becomes the `id`, and the GitHub slug becomes a preceding empty
      `<a id>` when it differs. Without an explicit anchor, the slug is the `id`.
    - The used-ids set is kept per `renderMarkdown` call; duplicates get `-1`, `-2`, ….
  - **Link targets:** links with a scheme keep `target="_blank" rel="noopener"`. A `#…`
    href, or one that `opts.rewriteLink` changed, renders without `target`, so it opens in
    the same tab.
- **`wwwroot/specs.html`**
  - **Non-markdown files:** a path that doesn't end in `.md` renders through
    `plainFileHtml`, an escaped `<pre class="spec-file"><code>`. For `.yml`/`.yaml`, a
    `#` at line start or after whitespace, outside quotes, starts a dimmed
    `.yaml-comment` span.
  - **Scrolling:** `scrollToHash()` runs after a markdown render and on `hashchange`. It
    looks the id up inside `#specs-view` only.
- **`wwwroot/assets/style.css`**
  - `.md-table`: auto width, bordered cells, a `--bg-raised` header row, and no row-hover
    tint.
  - `.yaml-comment`: muted colour.
- **Specs**
  - `cockpit/specs.md`: non-markdown files under Browsing, a new Anchors section, and the
    same-tab rule under Cross-links.
  - `cockpit/chat.md`: a new "Markdown rendering" section (`{#markdown-rendering}`) for
    the shared renderer (tables, heading ids), and the tab rule under Link safety.
  - `cockpit/command.md`: the `#anchor` exception, plus a link to that section.

## Live verification

I ran the cockpit from source (`dotnet run -- --config /tmp/mf22/mf-cockpit.yml`, port
5322, project `Magnaflow` = this repo) and drove it with headless Chrome through
playwright-core:

- **`concepts/machine-config.md`:** the axis table renders as a 4-column table (Axis | File
  | Lives | Holds), with `magnaflow.yml` as inline code in a cell. No raw `|---` text
  remains. The screenshot shows bordered cells in the dark theme.
  - At a 380 px viewport the table scrolls inside its wrapper (scrollWidth 845 in a 318 px
    box).
- **`concepts/machine-config.md#lookup-order`:** the H3 "Lookup order" sits at the top of
  the viewport (top 0, scrollY 1159), and its text shows no `{#lookup-order}`.
- **`concepts/watch-supervision.md#lock-file`:** the H3 "Lock file" sits at top 0 (scrollY
  538).
- **`run/mf-run.md`:** the "machine and project config" link has
  `href=specs.html?p=Magnaflow&path=concepts%2Fmachine-config.md` and no `target`.
  Clicking it navigated in the same tab (still 1 tab).
- **Same-page link:** a `#lookup-order` link on the open spec jumped within the page
  (scrollY 1194).
- **`config.yml`:** shown as `pre.spec-file`, with no headings and 8 dimmed comment lines.
  The YAML keeps its indentation and reads as YAML.
- **Command page** (`command.html?p=Magnaflow&id=0021-cockpit-bug-fixes`): renders as
  before (18 headings, 69 list items, 23 code blocks). Headings now carry ids
  (`context`, `task`, `what-changed`).
- **Chat page:** `renderMarkdown` called in page context renders a heading with an id, a
  list, a `_blank` https link and a table. `javascript:` is still plain text.
- **Page errors:** none.

## Git `Bin` check

`git diff --stat` against HEAD still prints `md.js | Bin 4366 -> 7766 bytes`. That is
expected: the old blob holds NUL bytes, and git calls a diff binary if either side is
binary. The new file itself is text: `git diff --no-index --stat /dev/null md.js` counts
203 lines. From the next commit on, diffs of `md.js` are readable.

## Not done / follow-ups

- **`assets/app.js` has the same defect:** a literal NUL in a Map key at line 523
  (`it.project + "\0" + it.id`). Because git treats the file as binary, it also keeps its
  CRLF line endings despite `.gitattributes`. It is outside this cmd's scope. Fixing it
  (`"\u0000"`) will make git renormalise the whole file to LF, a large but harmless diff.
  Worth its own small cmd.
- **Narrow-screen overflow on some specs, not caused by tables:** at 380 px,
  `machine-config.md` is still 491 px wide, because a paragraph contains long unbreakable
  inline-code paths (`tools/mf-watch/src/MagnaFlow.MfWatch/Config/…`). A
  `code { overflow-wrap: anywhere; }` would fix it but changes every page, so I left it.
- **No automated JS tests:** there is no JS test setup in the repo, and adding one would be
  a new dependency. The renderer was checked with ad-hoc node scripts plus the live run
  above.
- **Process note:** to check whether the 491 px overflow predates this change, I briefly ran
  `git stash` / `git stash pop` once. The working tree came back unchanged; nothing was
  committed or switched.

## Self-answered questions

- **Trailing `{#…}` only, or anywhere in the heading?** Anywhere. That is exactly what the
  lint's `ghSlug` strips (global regex), so the browser accepts the same anchors as the
  lint.
- **Slug of the raw markdown or of the rendered text?** The raw heading text, as the lint
  does. Backticks and asterisks are punctuation and get stripped anyway.
- **How does `md.js` tell a "spec cross-link" from other links?** A link that the page's
  `rewriteLink` changed, or a `#…` href, opens in the same tab. Every other link keeps
  `_blank`. No new option was needed. Chat and command pass no rewriter, so their links
  are unchanged; the command page's lane links already drop `target` in
  `rewriteLaneLinks`.
- **Other relative targets in specs (e.g. `../../tools/x.cs`)?** `specLink` returns them
  unchanged, so they keep `_blank`, as before.
- **Where to look up the hash target?** Only inside `#specs-view`, so a heading whose slug
  matches a page element id (`live`, `breadcrumbs`) still scrolls to the heading.
- **Is an unescaped `|` inside inline code a cell separator?** Yes, as in GFM. Only `\|`
  is literal.
- **Where to document the renderer's tables and ids?** In `chat.md`, which already
  described `md.js` (Link safety). `specs.md` and `command.md` link to the new section.
- **YAML touch: how much?** Comments dimmed only, with simple quote tracking. Any other
  non-`.md` file gets a plain escaped `<pre>`.
- **The `git diff --stat` check in task 6 can't show text against HEAD:** that check was
  done with `--no-index` against `/dev/null` instead, as explained above.
