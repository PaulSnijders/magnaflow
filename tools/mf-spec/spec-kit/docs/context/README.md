# Context — what the outside world said

<!-- Shipped by the spec kit. Read this before adding or editing a file here. -->

Material from outside this repo that later work refers to: a customer
mail, meeting notes, a vendor's answer about a limit, a data request.
Filed so the next person finds what was actually said instead of a
second-hand memory of it.

Context is **managed, not only appended**. A folder where one
conversation lies scattered over five fragments, and a mistake stays
because correcting was not allowed, is worse than one that is kept
right. A mistake left standing costs more than removing it.

- Filename: `YYYY-MM-DD-slug.md` — the ISO date of the material itself
  (the first message, the day of the meeting), not of filing; slug of
  2–5 words, subject before form. No subfolders.
- **One topic or conversation is one file**, also when it runs over
  several days. Follow-ups are added to it; the filename keeps the date
  of the first message.
- **Correcting is expected.** Wrong names, dates, field and table names,
  dead links: put them right.
- **Cleaning up is allowed.** Empty messages, duplicate passages, leftover
  signatures and quoted replies do not belong here.
- **The substance stays.** Cleaning and merging never change what
  someone said. A decision that was later reversed stays, with what
  happened next added — that is the course of events, not noise.
  Correcting is making right what was copied wrong; not making the
  history tidier than it was.
- **Maintenance window** (guideline): up to a week old, edit freely; a
  week to a month, correct and clean up only; older, leave alone unless
  something is demonstrably wrong.
- **The diff is the account.** This folder is in git; every change to
  existing context is visible and revertible.
- Keep the load-bearing sentences verbatim and quoted, with who said them
  and when. Text only — link to attachments; no secrets.
- Frontmatter: `date` (of the first message), `topic`, `source` (who or
  what, via which channel). If it led to a choice, end with
  `Led to: decisions/NNNN-slug.md`.
- A quality-pass record (see `docs/specs/README.md#quality-pass`) is filed here
  as `YYYY-MM-DD-quality-pass.md`: the range reviewed, the findings with
  their verdict.
- Source material, not reasoning: the weighing goes in `docs/decisions/`,
  the resulting behavior in `docs/specs/`.

This folder may stay empty forever; `ls` is the index.
