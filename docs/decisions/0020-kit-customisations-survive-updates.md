---
date: 2026-10-28
topic: mf-spec, spec-kit, update
status: accepted
---

# 0020 — Kit customisations survive updates: PROJECT.md and a marked CLAUDE section

## Situation

Another adopter changed their kit: their own wording in the CLAUDE
section and a branch-per-cmd rule in the specs README's `### Branches`.
Our update overwrites kit-owned files and re-merged the CLAUDE section
by judgement. The README change would have been lost without a word,
and the merge produced a differently worded section in every repo (the
1.3 update already had to remove old blocks "whatever their wording").
Their fork introduced begin/end markers around the CLAUDE section.

## Options

1. **Markers in every kit file**, with a project part below the end
   marker. Most project changes alter a kit rule ("branch by default"
   instead of "no branch"); appending leaves two contradicting rules in
   one file, and nothing says which wins.
2. **Config switches** for the known variation points. Only covers what
   we foresaw; every new deviation needs a kit release.
3. **One project-owned `docs/specs/PROJECT.md`** that wins over the kit
   files, headings named after what they change; kit files stay
   verbatim. Markers only in the root CLAUDE.md, the one file the kit
   shares with the project.

## Choice

Option 3, kit 1.5. Kit files are replaced verbatim and point to
PROJECT.md; the CLAUDE span is replaced verbatim and project rules live
outside it. As a safety net the update compares each kit file with the
new kit before overwriting and moves project edits into PROJECT.md. It
costs no context: PROJECT.md is read with the README or a command, not
every turn. Every "From X to Y" step now applies whatever the version
stamp says, because the fork stamped its own "1.2".
