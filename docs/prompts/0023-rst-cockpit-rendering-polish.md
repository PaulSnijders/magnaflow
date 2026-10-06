---
title: "mf-cockpit: anchors clear the sticky bar, multi-line list items, app.js as text, long code wraps"
cmd: 0023-cmd-cockpit-rendering-polish.md
done: 2026-10-06
summary: "Anchors now land below the sticky summary bar, wrapped list items render as one item (with nested lists), app.js is a plain LF text file again, and spec pages fit a 380 px screen. Nothing to do beyond the usual install.ps1 rollout."
---

# Report

All six task items are done. `spec_lint` reports 0 problems, `dotnet build` is clean, and
`dotnet test` for mf-cockpit passes (311/311). I verified the change live against a cockpit run
from source (see below).

## What changed

- `wwwroot/assets/app.js`
  - **NUL removed.** The Map key is now `it.project + "\u0000" + it.id`. The whole file is
    rewritten with LF endings (it had 645 CRLFs). The diff is whole-file, as expected.
  - **Bar height variable.** `Cockpit.summaryBar()` writes the bar's real height into the CSS
    variable `--summary-bar-h` on `<html>`. It does this once right away and again through a
    `ResizeObserver` on every resize.
  - **Re-alignment.** When the bar's height changes and the `location.hash` target is now
    covered by it, the target is scrolled into view again. If several elements have that id,
    the last one wins, because rendered markdown comes after the page chrome. See "Live
    verification" for why this is needed.
- `wwwroot/assets/style.css`
  - `:root { --summary-bar-h: 0px }` is the fallback for pages without a bar.
  - `:is(h1…h6)[id], a[id] { scroll-margin-top: calc(var(--summary-bar-h) + 0.5rem) }`.
  - `code { overflow-wrap: anywhere }`, with `pre code` reset to `normal`. `pre` already wraps
    with `pre-wrap`.
  - `body { overflow-wrap: break-word }`. See the decisions below.
- `wwwroot/assets/md.js`: lists are rebuilt as a stack of open lists. Each item is
  `{ text, contentIndent, subs }`.
  - **New marker line.** A marker indented to the open item's content column or deeper starts
    a nested list. Otherwise the stack pops back to the marker's level. At the same level a
    different marker type (`ul` vs `ol`) starts a sibling list.
  - **Continuation line.** An indented non-marker line continues the deepest item it is
    indented past, joined with one space. Inline markdown runs on the joined text, so a
    `` `code` `` split across the line break works. A non-indented line ends the list, as
    before.
  - **Blank line.** It keeps the list open only when the next non-blank line is indented.
  - The header comment states the rules and the indented-fence limit.
- **Specs**
  - `docs/specs/cockpit/chat.md#markdown-rendering`: the heading-ids bullet now says scroll
    targets clear the summary bar. New **Lists** bullet (continuation, blank-line rule, nesting,
    fence limit). New **Long words wrap** bullet.
  - `docs/specs/cockpit/specs.md#anchors`: a hash load and a same-page link both land just
    below the sticky summary bar.

## Live verification

I ran `dotnet run -- --config /tmp/mf23.yml` (port 5299, this repo as project `magnaflow`) and
drove it with headless Chrome (`/usr/bin/google-chrome` via a cached playwright-core). No
console or page errors on any page.

- **`concepts/watch-supervision.md#lock-file`, 1280 px:** bar bottom 37 px, "Lock file" top
  44 px, so it is fully visible.
- **Same-page link:** I injected a link to `#the-files` on that page and clicked it. Heading top
  44.7 px, bar bottom 37 px.
- **Extra `<a id>` target:** no spec has a `{#slug}` that differs from its GitHub slug, so I
  rendered `## Other slug {#explicit}` into the page and scrolled to `#other-slug`. Top 96.7 px,
  bar bottom 88.9 px (380 px viewport).
- **380 px, first attempt:** the hash load landed with the heading at 45 px while the bar ended
  at 89 px, so it was hidden. The bar is one line (37 px) when the spec renders and wraps to
  89 px in the same frame, once its counts arrive. The scroll had already used the old height.
  - Fix: the `ResizeObserver` re-alignment described above.
  - After the fix: heading top 96.5 px, bar bottom 88.9 px. The screenshot shows "Lock file"
    right under the bar.
- **`concepts/machine-config.md` list:** "Each tool reads only its own section and ignores every
  other key. A future tool claims a new section without touching the others." is one `<li>`
  of a 4-item list. The next element is the `H3` "Lookup order". There is no stray paragraph.
- **380 px, no horizontal scroll:**
  - `machine-config.md`: `scrollWidth` was 491 before the `body` rule and is 380 after.
  - All 24 files under `docs/specs/` (excluding README): none wider than 380 px.
  - Tables still scroll inside their `.table-wrap`.
- **Lane, command and chat pages:**
  - `index.html`, `project.html?p=magnaflow`, the command pages for 0022 and 0021, and
    `chat.html?p=magnaflow` load with their usual content and no errors.
  - The 0022 command page still renders its plan/report lists, and continuation lines are now
    joined into their items.

## Git `Bin` check

- `git diff --stat` on `app.js` now shows `652 insertions(+), 645 deletions(-)`, not `Bin`.
- A simulated next change (the new file plus one appended line, via `git diff --no-index
  --stat`) shows `1 +`.
- `git ls-files --eol` shows `w/lf attr/text=auto eol=lf`. The index still says `i/-text`
  until this is committed.
- No file under `wwwroot/` contains a NUL byte any more. Only the `.png`s were skipped.

## Not done / follow-ups

- **Indented fenced code under a list item** is out of scope. Fences are lifted out before line
  parsing, so such a block ends the list and its content keeps its indentation. No current spec
  needs it.
- **Ordered list start numbers** are still not carried over (`3.` as the first item renders as
  1). This is unchanged behaviour.
- **The re-alignment** only fires when the bar resizes while the hash target sits under it. A
  user who has scrolled elsewhere is never moved.
- **No automated tests for `md.js`.** The project has no JS test harness. I checked the list
  cases with an ad-hoc node script (bullets, numbered, nested, mixed types, blank-line
  continuation, lazy non-indented line). All 127 tracked `.md` files render without throwing.

## Self-answered questions

- **How to get the bar's "real height"?** Measured from the element and written into
  `--summary-bar-h` by a `ResizeObserver`, not a fixed 37 px. The bar wraps to 89 px at 380 px.
- **Should a hash load be re-aligned when the bar grows after the scroll?** Yes. Without it the
  narrow-screen case in task 5 fails. It only re-aligns a target the bar currently covers.
- **The 380 px overflow was not inline code.** It was the plain-text `Code:` line at the end of
  `machine-config.md`, a long run of comma-separated paths. Task 4's `overflow-wrap: anywhere`
  on `code` alone did not fix it, so I added `overflow-wrap: break-word` on `body`.
  `break-word` was chosen over `anywhere` because it leaves min-content sizing alone, so tables
  and flex layouts are unchanged. This is slightly wider than the task's literal wording, but
  it is what the stated goal ("fits a 380 px viewport") needs.
- **A blank line followed by indented text:** stays in the same item, joined with a space. The
  cmd only defines blank-then-non-indented as the end. Multi-paragraph items are not needed in
  these specs.
- **Non-indented text directly after an item, with no blank line:** ends the list, as the cmd
  says. This is not CommonMark's lazy continuation.
- **Inline code inside tables:** it wraps as well. The table keeps its scroll wrapper for
  whatever is still too wide.
