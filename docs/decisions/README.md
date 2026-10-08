# Decisions — why it became this way

<!-- Shipped by the spec kit. Read this before adding a file here. -->

One choice and the weighing underneath it: what the situation was, which
options were on the table, what they were measured against, what was
chosen. Only choices a future reader would ask "why is it like this?"
about — a data model, a pricing rule, a library, a deliberate "we looked
and did nothing".

- Filename: `NNNN-slug.md`. Read the next number off disk (highest
  present + 1, never from memory); slug of 2–5 words naming the *choice*.
  No subfolders — use `topic:` in the frontmatter for retrieval.
- **A record, not a spec.** When a choice is revisited, write a new
  decision that supersedes it: `supersedes: NNNN-slug.md` in the new
  file; in the old one flip `status:` to `superseded` and add one pointer
  line under its title. Correcting a moved link, a plainly wrong figure
  or something that should never have been committed is fine.
- Cited, not copied: the owning concept or page spec carries one line —
  `Why: decisions/0012-invoice-rounding.md` — and never repeats the
  argument. The decision never restates the current truth; that is the
  spec's job.
- Frontmatter: `date`, `topic` (comma separated), `status: accepted |
  superseded`, optional `supersedes`. Then `# NNNN — <title>` and four
  sections: **Situation**, **Options** (only the ones genuinely on the
  table), **Measurement** (what decided it), **Choice**.
- **One choice, a few hundred words.** Past ~1,000 you are writing a
  design or an investigation. A design belongs in the specs (spec ahead
  of code is a work order); several independent choices are several
  decisions, or are small enough to live in the cmd.
- **An investigation is not a decision.** The research that led to a
  choice — measurements, comparisons, a spike — stays where it was done:
  the cmd's `rst` report, or `docs/context/` when it is material from
  outside. The decision cites it under Measurement and keeps only the
  figures that decided it.

This folder may stay empty forever; `ls` is the index.
