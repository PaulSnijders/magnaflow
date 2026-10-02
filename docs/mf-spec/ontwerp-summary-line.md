# rst summary line — design (2026-08-17)

The lane produces more prose than anyone reads. A cmd is long because it
was designed in a conversation; a pln is long because it argues; an rst
is long *by design* — `v0.2-direction.md` pins it as "written for the
next Cowork session to read". That is correct for an AI reader. The gap
is that the human is a reader too, and there is no layer at human size:
to know what a finished command actually did, you open the rst, or paste
it into a chat and ask for a summary. That summary is the thing the
system should have produced itself.

## Decision

Every rst carries a one-line `summary:` in its frontmatter, and the
cockpit renders it wherever the command is listed. Nothing else about
the report changes — the long body stays exactly as it is, for the
reader it was written for.

Why frontmatter and not the first paragraph of the body: the cockpit
already reads frontmatter and nothing else for cmd files
(`CmdFileLite`), so a summary there costs one small reader and no body
parsing. It also keeps the rule mechanical — a missing field is visibly
missing, a "first paragraph" convention silently drifts into three
paragraphs.

Why one line and not "keep it short": length only stays short when
something structural holds it there. The renderer gives the summary one
line and truncates; that is the constraint, not the instruction.

## Shape

```yaml
---
title: "..."
cmd: 0009-cmd-rst-summary-line.md
done: 2026-08-17
summary: Lane rows now show one line per finished command; the long report is unchanged.
---
```

Plain text, no markdown, one or two sentences. It answers one question:
*what changed, and is there anything I need to do?*

## Who writes it

The agent, as part of the rst it already authors — the instruction lives
where the rst instruction already lives (`PromptBuilder`, and the kit's
conventions README). The controller's fallback report
(`RstFile.Write`/`WriteFallback`) writes one too; today that fallback
writes no frontmatter at all, which is its own small inconsistency with
the format every hand-written rst follows.

## Where it shows

`project.html`'s lane row, under the title, greyed — the place where you
scan a project. And at the top of `command.html`, above the report body.

Absent on older reports; those simply render as they do today.

## Not in scope

- **The pln.** It is read once, at the gate, while you are deciding —
  not repeatedly afterwards. Same mechanism would fit later; the pain
  observed is the rst.
- **The index's Latest column.** Already dense, and it is a
  cross-project glance, not a read.
- **Any lint or length enforcement.** The renderer's single line is the
  only enforcement worth having until a summary actually misbehaves.
