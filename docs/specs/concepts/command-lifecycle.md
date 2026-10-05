# Technical

The delta lane in `docs/prompts/`: which files make up one command, the
frontmatter that carries its state, and who may move it from one status
to the next. The lane itself is described in
[the conventions](../README.md#prompts--the-delta-lane-docsprompts); this
file pins what the tools rely on.

## Files and identity

One command is up to four flat files sharing `NNNN-name`, the command id:

| File | Written by | When |
|---|---|---|
| `NNNN-cmd-name.md` | human (or cockpit) | always; the anchor of the set |
| `NNNN-pln-name.md` | agent | only for a plan round with an open question, rewritten in place on re-plan |
| `NNNN-qa-name.md` | worker appends questions, human answers | only once a question arose; later rounds append |
| `NNNN-rst-name.md` | agent (worker fallback) | at a terminal outcome |

- **Id grammar**: `^\d{4}[B-Z]?-[a-z0-9-]+$`. Ids sort ordinally, so a
  follow-up sits between its parent and the next number
  (`0005-x < 0005B-x < 0006-y`). That order is the queue order.
- **Follow-ups** keep the parent's number plus one letter `B`..`Z`. The
  letter is grouping only; context continuity comes from `resume:`
  ([session resume](session-resume.md)).
- **Numbering** is read off disk every time: highest number present + 1.
  The cockpit's `NextFreeNumber` and `NextFreeSuffix` do exactly that.
- A pln/qa/rst without its cmd file is reported as a malformed lane
  entry (`orphaned sibling`), never guessed at. A scan never crashes on
  one bad file.
- A pln therefore means exactly one thing: this command needed a human.

## cmd frontmatter

| Key | Meaning |
|---|---|
| `status` | required; one of the statuses below; missing or unknown makes the command malformed |
| `title` | optional; falls back to the name slug with spaces |
| `created` | informational |
| `branch` / `base` | work branch and its base; `base` without `branch` is malformed. See [worker run](worker-run.md#branches) |
| `resume` / `group` / `fresh_session` | session continuity; `resume` with `fresh_session: true` is malformed |
| `specs` | list of repo-relative files; a missing one refuses the run (exit 2) |
| `attempts` | written by the worker; cumulative across runs and pauses, reset by a re-run (below) |
| `max_attempts` | per-command override of `defaults.max_attempts` |

The worker rewrites only the `status:` and `attempts:` values, in place.
Comments, key order, a UTF-8 BOM and the body survive byte for byte. A
missing `attempts:` is appended as the last frontmatter line. The body
is human-owned; the agent is told never to edit the cmd file.

## rst frontmatter

Exactly `title`, `cmd` (the cmd filename), `done` (`YYYY-MM-DD`) and
`summary:`: one line of plain text, quoted when it contains a colon. The
cockpit renders `summary:` in the lane. The worker's own fallback rst
carries the same four keys, so a missing agent report is still readable
in the lane. A post-run warning (e.g. `mf-run start` failed) is appended
as a `## Warning` section, never overwriting the report.

## pln and qa formats

- The pln gate is the heading `## Open questions (this round)`, one
  bullet per question. Any bullet under it pauses the run. A pln without
  that section, or no pln at all, means nothing is open.
- **One question** is one top-level bullet together with everything
  under it: its continuation lines, its nested bullets, and any text
  after the list (a recommendation paragraph belongs to the last
  question), up to the next top-level bullet or the end of the section.
  The top level is the indent of the section's first bullet. Text before
  the first bullet is not a question. A question with nested options
  (A) and (B) is therefore one question, not three.
- The qa file grows by `## Question (round N)` blocks, each followed by
  an `**Answer**:` line the human fills in. Each question lands in its
  own block verbatim, minus its top-level `- ` marker, so nested lines
  stay nested. Rounds continue numbering across pauses; there is never a
  second qa file.

## Statuses and who moves them

```text
draft ─► ready ─► running ─► done
           ▲         ├─────► aborted
           │         ├─────► questions ─► (human answers) ─► ready
           └─────────┴─ mf-run stop failed: back to ready
```

| Transition | Actor |
|---|---|
| create `draft` | human or design session (by hand), cockpit (New draft, Follow-up) |
| `draft → ready` | human by hand, or the cockpit's Ready button (exactly this one flip, 409 otherwise) |
| `ready → running` | worker, as its first commit |
| `running → questions / done / aborted` | worker only |
| `running → ready` | worker, only when `mf-run stop` fails before implementation |
| `questions → ready` | human, after answering in the qa file |
| stuck `running → ready`, `aborted → ready` | human, by hand |

- `draft` is invisible to every tool that executes; nothing executes it.
- The worker runs only `ready`. A `running` command is refused with a
  hint that an earlier run was probably interrupted. Resetting it is a
  human decision.
- mf-watch only reads statuses and the worker's exit code. It never
  writes one ([watch supervision](watch-supervision.md)).
- `aborted` is a normal terminal status, not an exception. Setting it
  back to `ready` is the human's "try again". A run that starts from
  `ready` with `attempts >= max_attempts` resets `attempts:` to 0 and
  gets the full budget again. Its claim commit deletes the old rst
  (which stays in git history), so the new run's rst replaces it, and
  the first line under its "What was done" notes that this is a re-run
  after an earlier `aborted`.
- A resume from `questions` → `ready` is not a re-run: it continues the
  same run and keeps counting. A pause always leaves attempts unspent,
  so it never meets the reset.
- An abort reason always names the last failing phase (`plan`, `agent`,
  `build`, `tests`). If no phase ran at all, it says so.

## Commits

Every transition is committed; files never move. Commit messages in use:

| Writer | Message |
|---|---|
| worker claim | `mf-worker: <id> -> running` |
| worker pause / refusal | `mf-worker: <id> -> questions`, `mf-worker: <id> -> ready (<run command> stop failed)` |
| worker terminal | `command <id>: <title>` (folded branchless run), else `mf-worker: <id> -> done\|aborted (N attempt(s))` |
| cockpit | `cockpit: create draft <id>`, `cockpit: ready <id>`, `cockpit: create follow-up draft <id> (from <parent>)` |

The conventions and [design](design.md#iii-git-is-the-database) say
bookkeeping commits use the `lane:` prefix with `[skip ci]`. No tool does
that today: `git log --invert-grep --grep="^lane:"` filters nothing
automated. Only hand-made bookkeeping commits follow it.

Bookkeeping commits land on the branch the worker was invoked from, never
on a work branch ([worker run](worker-run.md#branches)).

DRAFT: generated from code, not human-reviewed.

Code: tools/worker-controller/src/MagnaFlow.WorkerController/Prompts/CmdFile.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Prompts/CmdStatus.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Prompts/PromptScanner.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Prompts/PlnFile.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Prompts/QaFile.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Prompts/RstFile.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Prompts/DraftWriter.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Prompts/LaneScanner.cs
Why: decisions/0005-worker-v0-2-direction.md, decisions/0015-rst-summary-line.md
