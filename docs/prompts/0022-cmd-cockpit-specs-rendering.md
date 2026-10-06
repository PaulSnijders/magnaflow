---
title: "mf-cockpit: the specs page renders tables, anchors and non-markdown files properly"
status: done
created: 2026-10-06
attempts: 1
---

## Context

The specs browser (`specs.html`) is where a human reads the state
specs, and today it renders them badly. Seen live on 2026-10-06 in
`specs.html?p=Magnaflow&path=concepts%2Fmachine-config.md` and
`…&path=config.yml`:

- **Tables** appear as raw `| Axis | File | … |---|` text. `md.js` has no
  table support, and the specs use tables a lot.
- **Anchors do nothing.** Rendered headings get no `id`, so
  `concepts/machine-config.md#lookup-order` opens at the top. The
  explicit anchor syntax the kit allows (`### Lookup order {#lookup-order}`,
  see `scripts/spec_lint.mjs`) shows up literally in the heading.
- **Non-markdown files are rendered as markdown.** `config.yml` is passed
  through `renderMarkdown`, so every YAML comment (`# …`) becomes a big
  heading and the YAML itself runs together into one paragraph.
- **`md.js` contains literal NUL characters** as code-block placeholders
  (`markerFor`), so git treats the file as binary and its diffs cannot be
  read or reviewed.

## Task

1. **Tables in `md.js`.** Render GitHub-style pipe tables:
   - a header row, a `|---|` delimiter row (`:---`, `---:`, `:---:`
     alignment allowed), and body rows;
   - inline markdown inside cells (code, emphasis, links) works as
     elsewhere, and links go through the same link vetting;
   - `\|` inside a cell is a literal pipe;
   - style the table in `style.css` to fit the existing dark theme
     (borders, padding, header row), and wrap it so a wide table scrolls
     horizontally instead of breaking the layout on a narrow screen.
2. **Heading anchors in `md.js`.** Use the same rule as
   `scripts/spec_lint.mjs` (`ANCHOR`, `ghSlug`), so every anchor the lint
   accepts also works in the browser:
   - a trailing `{#kebab-slug}` is removed from the visible heading text
     and becomes the heading's `id`;
   - every heading is also reachable by its GitHub-style slug of the
     visible text (when it differs from the explicit anchor, add it as a
     second target, for example an empty `<a id>` before the heading);
   - ids are unique within the page; a duplicate gets `-1`, `-2`, … like
     GitHub.
3. **Scroll to the anchor** in `specs.html`: after a spec renders, scroll
   to the element named by `location.hash`, and do the same when the hash
   changes. A same-page link (`#why`) jumps within the page.
4. **Spec cross-links open in the same tab.** A rewritten spec link
   (`specs.html?p=…&path=…`) is navigation inside the cockpit; open it in
   the same tab. External links (`http:`, `https:`, `mailto:`) keep
   `target="_blank"`.
5. **Non-markdown files** in `specs.html`: a file whose name does not end
   in `.md` (for example `config.yml`) renders as a preformatted, escaped
   code block, not as markdown. Keep it simple: no new dependency. A
   minimal YAML touch is welcome (comments dimmed), but plain `<pre>` is
   acceptable.
6. **No NUL characters in `md.js`.** Replace the literal `\0` characters
   with the `"\u0000"` escape (or another placeholder) so the file is
   plain text; check with `git diff --stat` that git no longer reports it
   as `Bin`.
7. **Verify live**, against a cockpit run from source with a scratch
   config, and write what you saw in the rst:
   - `concepts/machine-config.md`: the axis table renders as a table;
   - `concepts/machine-config.md#lookup-order` scrolls to "Lookup order",
     and the heading shows no `{#lookup-order}`;
   - `concepts/watch-supervision.md#lock-file` likewise;
   - a link from `run/mf-run.md` to another spec opens in the same tab;
   - `config.yml` shows as readable YAML;
   - chat and command pages still render their markdown as before.
8. **Spec-first:** update `docs/specs/cockpit/specs.md` (tables, anchors,
   same-tab spec links, non-markdown files) and, if the renderer's
   behaviour for chat/command changes visibly (tables, anchors),
   `docs/specs/cockpit/chat.md` and `docs/specs/cockpit/command.md`.

## Not in scope

- Changing the specs themselves: `{#…}` anchors are a kit convention and
  stay.
- Full CommonMark, syntax highlighting for code, or a markdown library.
