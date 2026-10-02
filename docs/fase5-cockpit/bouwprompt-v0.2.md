# Build prompt — mf-cockpit v0.2 (follow-up commands + polish)

Copy-paste the block below into Claude Code, from the repo root.

---

Extend `tools/mf-cockpit/` per the items below. Read
`docs/fase5-cockpit/ontwerp-v0.1.md` (the design and its invariant:
every write is human-initiated, lands in the existing plain-text/git
formats, and stays reproducible by hand) and
`docs/fase5-cockpit/v0.1-completion-notes.md` first. The main
feature also touches the id scanners in `tools/worker-controller/`
and `tools/mf-watch/` — read their scanner code before changing
anything.

## 1. Follow-up commands (`0005` → `0005B` → `0005C`)

A command often needs a short second round: "good, but this line
should be different." Today that means a whole new command with a
cold context. New mechanism:

- **Naming**: a follow-up to `0005-fix-lava` is
  `0005B-cmd-<new-slug>.md`; a follow-up to that is `0005C-...`, and
  so on (parent is implicitly A; suffix uppercase). ASCII sorting
  places these exactly between parent and `0006-*` — execution order
  and visual grouping come for free. The suffix is a **human-facing
  convention only**: no tool may parse meaning out of it.
- **Machine truth is frontmatter**: the cockpit's "continue on this
  command" button (on `command.html`, sensible only for terminal
  statuses `done`/`aborted`) opens a small form (feedback text +
  optional slug) and creates the follow-up draft with: frontmatter
  copied from the parent (`branch`, `base`, `group`, `specs`),
  `resume:` set from the parent's `.magnaflow/<parent-id>/
  session.yml` so the worker continues the SAME Claude session, and
  `status: draft`. Body: the feedback text, plus a one-line
  reference to the parent cmd and its rst. Committed immediately,
  like the other writes. **Verify the exact `resume:` semantics in
  the worker controller's source (FR-013a) before wiring — if it
  expects something other than a raw session id, follow the worker,
  don't invent.** If the parent has no session.yml, still create the
  draft but omit `resume:` and surface a visible warning in the
  response ("no recorded session — will start cold").
- **Suffix allocation**: next free letter per parent (B, then C, …);
  409 when the parent id doesn't exist. Plain new drafts keep using
  next-free-`NNNN` and must **ignore** suffixed siblings when
  computing it.
- **Scanner compatibility — the critical part**: the id grammar
  widens from `\d{4}` to `\d{4}[B-Z]?`. Find every place that
  parses, groups, sorts, or validates lane ids and update all three
  tools: the worker controller (`CmdFile`/`PromptScanner` and
  anything deriving evidence paths `.magnaflow/<id>/`), mf-watch
  (`PromptStatusScanner`), and the cockpit (`LaneScanner`,
  `DraftWriter`). Each tool keeps its own scanner (no shared library
  — existing decoupling rule). Add unit tests in each tool's own
  suite proving a suffixed command is scanned, dispatched, and
  evidence-pathed identically to a plain one.
- The cockpit's lane views group a parent and its follow-ups
  visually (indent or badge — keep it subtle), sorted by full id.

## 2. Polish

- **Pretty-print JSON logs**: in the evidence tails on
  `command.html`, detect lines that are single JSON objects (as in
  `claude.log`'s stream-json) and render them pretty-printed and
  collapsible (client-side, in the vendored JS — still no CDN).
  Non-JSON lines render as now. The raw text stays available
  (toggle), and the bounded-tail guards are unchanged.
- **Clickable breadcrumb**: the crumb path at the top of every page
  becomes links (cockpit → project → command/chat/specs). Current
  page stays plain text.
- **Chat discoverability**: `chat.html` shipped in v0.1 but no page
  links to it. Add it to the shared nav/breadcrumb on the project
  and command pages (respect `chat.enabled`), and on `command.html`
  place the "continue on this command" feature so chat and
  follow-up are both one click from a finished command.

## Requirements

- Tests in the existing styles: unit tests for suffix allocation,
  scanner grammar (all three tools), and follow-up frontmatter
  copying; a `WebApplicationFactory` integration test for the
  follow-up endpoint (creates `0005B-...` with `resume:` from a
  seeded session.yml, commits, 409 on unknown parent, warning path
  when session.yml is missing); UI changes verified with the live
  browser check (`/browse`) as in v0.1, console clean.
- No behavior change for existing plain-`NNNN` lanes — run all
  three tools' full suites.
- If the worker's `resume:` handling contradicts what this prompt
  assumes, or any scanner change risks breaking mf-watch's dispatch,
  stop and ask rather than guess.

When done, append a v0.2 section to
`docs/fase5-cockpit/v0.1-completion-notes.md` or write
`docs/fase5-cockpit/v0.2-completion-notes.md` in the same style:
what shipped, validation, deviations, left for later. Also update
`docs/fase5-cockpit/ontwerp-v0.1.md`'s action list (the invariant
section): the follow-up draft is write #3, same rules.
