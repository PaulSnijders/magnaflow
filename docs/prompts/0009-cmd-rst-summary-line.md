---
title: "Lane: a one-line summary per report, written into the rst and shown in the cockpit"
status: done
attempts: 1
created: 2026-08-17
---

## Context

To find out what a finished command did, you have to open its rst — the
report is deliberately written for the next AI session, not for a human
scanning a project. There is no human-sized layer. Design:
`docs/decisions/0015-rst-summary-line.md`.

## Task

1. **Format.** The rst frontmatter gains `summary:` — one line, plain
   text, one or two sentences, no markdown. Document it in the kit's
   conventions README (`tools/mf-spec/spec-kit/docs/specs/README.md`,
   the "Prompts — the delta lane" section) next to the existing
   description of what the rst is for, bump the version stamp and add
   the KIT.md migration note the way every kit change does. Mirror the
   one-line description in `tools/mf-spec/system.md`'s delta-lane block.

2. **Worker.** The rst instruction in
   `tools/worker-controller/src/MagnaFlow.WorkerController/Execution/PromptBuilder.cs`
   tells the agent to include `summary:` and what it is for. `RstFile.Write`
   currently writes a body with no frontmatter at all — give it the
   frontmatter every hand-written rst has (`title`, `cmd`, `done`,
   `summary`), with `WriteFallback` supplying its own one-liner.

3. **Cockpit.** `LaneScanner` already records `RstPath`; read that file's
   frontmatter `summary` only (a small reader beside `CmdFileLite` — not
   the body, which can be long) and expose it on `LaneItem`. Render it:
   `project.html` shows it greyed under the command title in the lane
   row, truncated to one line; `command.html` shows it above the report
   body. A missing or empty `summary` renders nothing — every existing
   rst is in that state.

4. **Tests** in each tool's existing style: the reader (present, absent,
   malformed frontmatter), `RstFile` round-trip including the fallback,
   and the lane exposing the summary.

## Not in scope

The pln file, the index's Latest column, and any lint or length
enforcement — see the design doc's "Not in scope".
