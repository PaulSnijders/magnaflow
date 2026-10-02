---
title: "Lane: a one-line summary per report, written into the rst and shown in the cockpit"
cmd: 0009-cmd-rst-summary-line.md
done: 2026-08-18
summary: Every new report now carries a one-line summary in its frontmatter, shown greyed in the cockpit's lane and above the report itself; older reports render as before. Kit version 0.10.
---

## What was done

**Format (kit).** `tools/mf-spec/spec-kit/docs/specs/README.md` gained an
`rst-` frontmatter block in "Prompts — the delta lane", right after the
paragraph describing what the rst is for, with the `title`/`cmd`/`done`/
`summary` shape and the rule for `summary:` (one line, plain text, one or
two sentences, no markdown, quote a value containing a colon). Version
stamp 0.9 → 0.10 in that file and in `KIT.md`, with a 0.9 → 0.10
migration note (documentation-only, no repair — existing reports keep no
summary). `docs/mf-spec/system.md`'s `docs/prompts/` block mirrors the
one-line description.

**Worker.** `PromptBuilder.BuildInitial` now tells the agent to open the
rst with the four-field frontmatter and explains what the summary is for
(the human-sized layer; the cockpit shows it in the lane) plus the
explicit "the body stays what it would otherwise be — do not shorten it".

`RstFile.Write` writes that frontmatter itself instead of a bare body.
New optional `title`/`summary`/`done` parameters; when a caller supplies
none, `title` falls back to the file's own name slug (the same fallback
`CmdFile.Title` uses) and `summary` to the first line of `whatWasDone`,
so the field is never silently absent. `WriteFallback` supplies its own
one-liner — "Aborted after N attempt(s) and the agent wrote no report of
its own — read claude.log …" — and takes the command title, which
`TaskRunner` now passes from `cmd.Title`.

**Cockpit.** New `Prompts/RstSummary.cs`: streams an rst with
`File.ReadLines`, stops at the closing `---`, and deserializes only that
with YamlDotNet — the body is never read. `LaneItem` gained
`Summary`, filled by `LaneScanner` from the `RstPath` it already
records; `CommandSummaryDto` and `CommandDetailDto` carry it through.
`project.html` renders it as a greyed, single-line-truncated `div` under
the command title (full text in the `title` tooltip); `command.html`
renders it above the report body as plain text — never through the
markdown renderer. Both render nothing when it is null.

**Tests.** `RstSummaryTests` (present, quoted-with-colon, absent field,
no frontmatter, unclosed fence, invalid YAML, missing file, and a body
full of decoy `summary:` lines); `LaneScannerTests` for the lane
exposing it and leaving it null for a pre-convention rst and for a
command with no rst; an `ApiIntegrationTests` case for lane + detail;
`RstFileTests` for the exact frontmatter lines, the quoting fallback, the
title/summary defaults, and both fallback summaries; a `PromptBuilderTests`
case for the instruction. 149 worker-controller and 266 mf-cockpit tests
pass.

## Decisions and deviations from the plan

- **Quoting.** `RstFile` double-quotes a frontmatter value only when it
  would not survive as a plain YAML scalar (a `": "` sequence, a leading
  indicator character, a trailing colon, `" #"`, quotes, backslashes).
  A summary sentence with a colon in it is entirely normal prose, and
  unquoted it would turn the frontmatter the cockpit reads back into
  invalid YAML. Matches how existing hand-written reports are quoted
  (`title: "mf-run: robust + explainable status"`).
- **`summary` defaulting.** Rather than emitting the key only when a
  caller passes one, `Write` derives it from the first line of
  `whatWasDone`. The prompt asked for the frontmatter "every hand-written
  rst has", and a controller-written report is then as readable in the
  lane as an agent-written one.
- **`# Report` heading kept.** Hand-written reports have no such h1, but
  removing it was not asked for and `AppendWarning` uses it as its
  no-file fallback shape. Left alone.
- **Version 0.10, not 1.0.** Continues the existing 0.1…0.9 sequence;
  the update prompt's migration notes are an ordered list an AI reads,
  not a string comparison.
- **Frontmatter still shows in the rendered report body.** `command.html`
  passes the raw rst file to the markdown renderer, which knows nothing
  about frontmatter — so the `---`/`title:`/`summary:` lines render above
  the report as they already did for every hand-written rst. Pre-existing
  and out of this command's scope; noted because the new summary line now
  sits directly above it.
