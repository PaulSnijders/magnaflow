---
title: "mf-cockpit: anchors clear the sticky bar, multi-line list items, app.js as text, long code wraps"
status: ready
created: 2026-10-06
---

## Context

Follow-ups from the review of 0022, seen live in Chrome on 2026-10-06:

- **An anchor lands under the sticky bar.** `specs.html?…#lock-file`
  scrolls the heading to top 0, but `.summary-bar` is sticky and 37 px
  high, so the heading itself is hidden behind it.
- **Multi-line list items break.** The specs wrap lines at about 72
  characters, so many bullets continue on an indented next line:

  ```markdown
  - Each tool reads only its own section and ignores every other key. A
    future tool claims a new section without touching the others.
  ```

  `md.js` takes only the first line as the item and renders the
  continuation as a separate paragraph below the list. This affects most
  specs (`concepts/machine-config.md` shows it right after the first code
  block).
- **`assets/app.js` contains a literal NUL** (a Map key built as
  `it.project + "\0" + it.id`, see the 0022 rst). Git therefore treats the
  file as binary: its diffs cannot be read, and `.gitattributes`
  (`eol=lf`) is not applied, so the blob is still CRLF.
- **Long inline-code paths overflow on a narrow screen.** On a 380 px
  viewport `concepts/machine-config.md` is 491 px wide, because inline code
  such as `tools/mf-watch/src/MagnaFlow.MfWatch/Config/…` cannot break.

## Task

1. **Anchor offset.** Make a scrolled-to heading appear just below the
   sticky bar, for every page that renders markdown with ids. Use
   `scroll-margin-top` on rendered headings and the extra `<a id>`
   targets, tied to the bar's real height (a CSS variable is fine). Check
   both a load with `#hash` and a same-page `#…` link.
2. **List items with continuation lines** in `md.js`. A line indented
   under a list item belongs to that item, until a blank line followed by
   non-indented text, a new item, or a non-indented line. Join the
   continuation into the item's text (one space), so inline markdown
   across the line break still works. Nested sub-bullets (an indented
   `- `) render as a nested list. Numbered lists work the same way. A
   fenced code block indented under an item may stay out of scope if it
   gets complicated; say so in the rst.
3. **`app.js` as text.** Replace the literal NUL with the `"\u0000"`
   escape. Commit the file normalised to LF, as `.gitattributes`
   requires; the resulting whole-file diff is expected. Check that
   `git diff --stat` on the next change to `app.js` no longer says `Bin`,
   and that no other file under `wwwroot/` contains a NUL byte.
4. **Long inline code wraps** on narrow screens: let inline `code` (not
   `pre` blocks) break where needed (`overflow-wrap: anywhere` or
   equivalent), so a spec page fits a 380 px viewport without horizontal
   page scroll. Tables keep their own horizontal scroll wrapper.
5. **Verify live** against a cockpit run from source, and write what you
   saw in the rst:
   - `concepts/watch-supervision.md#lock-file`: the "Lock file" heading
     is fully visible below the bar;
   - `concepts/machine-config.md`: the bullet starting "Each tool reads
     only its own section" renders as one list item, with no stray
     paragraph after the list;
   - the same page at 380 px width has no horizontal page scroll;
   - lane, command and chat pages still render as before.
6. **Spec-first:** update the renderer section in
   `docs/specs/cockpit/chat.md` (`#markdown-rendering`) for list
   continuation, and `docs/specs/cockpit/specs.md` for the anchor offset
   if it describes scrolling.

## Not in scope

- Full CommonMark, a markdown library, or rewrapping the specs
  themselves.
