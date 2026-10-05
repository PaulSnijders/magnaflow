---
date: 2026-09-02
topic: specs, mf-spec, genres
status: accepted
---

# 0016 — Genres and the gate — design (2026-09-02)

> Migrated from `docs/decisions/0016-genres-and-gate.md` on 2026-10-05. Pre-dates the four-section
> decision format; original text unchanged.

Two things the spec system still lacks: a home for the genres that are
not specs, and a gate that carries the spec-first rule when nobody
remembers it. Both come out of one source: an evaluation of the
predecessor system (v1), used for two months in a repo by a second
developer without the builder present. That is the only real test of
this kind of system — whether it survives the person who invented it.

Judge every item here as a **kit** change, not a MagnaFlow change. The
kit's users are target repos, often client projects with a second
developer, a customer, and no builder present — exactly the situation
the evaluation measured. MagnaFlow's own repo is one user, and the
easiest one.

The evaluation's three failure modes:

1. what sits in the flow survives, what is an agreement evaporates;
2. a stale STATUS.md looks exactly like a clean one;
3. what has no house lands in `docs/` root.

Two of them are already handled. Failure mode 1 is why mf-spec moved
the burden into the change itself (spec-first, same turn, same commit)
and demoted drift to an audit — though nothing yet *enforces* that,
which is what the gate below is for. Failure mode 2 was closed by the
audit-state frontmatter and `ACCEPTED.md` in kit 0.12. Failure mode 3 is
untouched, and is the first half of this note.

## Five genres, five houses

| Genre | Folder | Answers | Life |
|---|---|---|---|
| Page spec | `docs/specs/<surface>/` | What does this screen do now? | mutates |
| Concept | `docs/specs/concepts/` | How does this mechanism work now? | mutates |
| Decision | `docs/decisions/` | Why did it become this, what was weighed? | frozen |
| Context | `docs/context/` | What did the outside world say? | maintained |
| Prompt/run | `docs/prompts/` | What was ordered, what came out? | frozen |

The rule that holds it together:

> Specs mutate, decisions and prompts do not. You update a spec when
> behavior changes. You never update a decision or prompt — you write a
> new one that supersedes it.

Context was frozen too until kit 1.1. That produced a folder of loose
fragments — one conversation over five files, mistakes left standing
because correcting was not allowed. Since 1.1 context is managed: one
conversation is one file, corrections and cleanup are expected, the
substance (including a later-reversed decision and what followed) stays,
and git's diff is the account. See the kit's `docs/context/README.md`.

That is the difference between current truth and history, and it is why
they cannot share a folder even when they are about the same subject.

### Decisions

A concept is short and describes how we want it. The weighing
underneath can be ten times as long, and today it lands in one of three
wrong places: inside a concept (which then mutates away from the
argument that justified it), buried in an rst (a report about
implementation, not about the choice), or as a loose `ontwerp-*.md`.
This repo's own `docs/fase*/` is the proof — the genre exists here,
unnamed and unnumbered.

`docs/decisions/NNNN-slug.md`. Numbered, not dated, because a decision
is **cited** and superseded: a concept ends with `Why: decisions/0012`,
and "0021 supersedes 0012" is a sentence that works with numbers and
not with dates. The number is identity; the date is metadata and lives
in the header and in git. Cost: read the next number off disk (same
rule as the prompt lane), and two branches can claim one number — a
five-second rename, and the lint catches duplicates.

Header `Date` / `Topic` / `Status`, sections Situation → Options →
Measurement → Choice. A concept carries the outcome plus the one
`Why:` line and never the argument; a decision carries the argument and
never the current truth, because that shifts while the document stands
still.

The `Why:` line is deliberately the same shape as the existing `Code:`
line on concepts, so this costs no new convention — just one more
trailing reference the lint can resolve.

### Context

ISO-dated: `2026-08-05-george-data-request.md`. Mail, meeting notes, a
customer's data question. Dated and not numbered because context has no
identity you cite — you search it chronologically or by keyword, and
numbering it is administration without a user. ISO sorts lexically =
chronologically, everywhere. State the rule explicitly, because without
it `05082026_name.md` appears by itself and sorts by day-of-month.

MagnaFlow has no outside world to record. Client projects are full of
one, and in the evaluation this material is exactly what pushed `docs/`
root from 8 to 23 files.

### Optional, but not absent

Both folders are created by the adopt prompt and may stay empty
forever. What makes an empty folder rot is not the emptiness — it is
that nobody knows what it is for. So each ships with a `README.md`
stating what belongs there, what does not, and the naming rule: **that
README is the house, and the folder is only where it stands.** An empty
`context/` with a five-line README is an invitation; an empty
`context/` without one is clutter, and the first person doing a cleanup
deletes it.

The index line per file (newest on top, written by the command that
creates the file) applies to `decisions/` and `context/`. It does not
apply to `prompts/`: the cockpit lane already is that index, generated,
and a hand-maintained duplicate goes stale in week one.

No subfolders. They demand a judgement on every new file ("is this
customer or technical?") and one file often belongs in two at once.
Keep it flat, use the `Topic:` header for retrieval
(`grep -l "Topic:.*planner"` replaces a subfolder and carries two
topics where a folder carries one), and split only above ~50 files —
then by year in `context/` only, which preserves sorting and asks no
judgement. `decisions/` never splits; the numbers run on.

## The gate

The improvement list's answer to failure mode 1 is CI: seven checks,
six blocking. The principle — replace the habit with a gate — is right.
The mechanism is not ours, and "six blocking" is the shape that teaches
people to route around a system.

**Where it lives.** MagnaFlow's gate is the worker controller: it
already runs build and test on every run, and mf-watch/cockpit is the
loop that reports. A `spec_lint` script in the kit, called by the
worker, fires whether or not a target repo has CI. A repo that does
have CI calls the same script — one implementation, two entry points.
The kit ships no script today; v1 had one.

**Hard — fails the run.** Only what is broken right now and hurts
someone outside the repo:

- a broken `# Technical` marker or the empty-help shape — this leaks
  admin content to end users, so it is a product bug, not a docs issue;
- a duplicate decision number, or a `decisions/NNNN` / anchor
  reference that does not resolve.

That is the whole blocking list. Each is objectively broken, fixable in
a minute, never a matter of judgement.

**Soft — never fails, always visible.** Everything that is a matter of
degree: a route without a spec, a commit that touched code without its
spec, `docs/` root growth outside the allowlist, audit freshness. These
raise the project's spec signal in the cockpit, which is a **level, not
a binary**: green / attention / stale, derived from unaudited commits
and open findings. A week without maintenance degrades the signal
visibly and costs nothing else; two months and the lane says so before
you open anything.

That is the whole point of the split: the pressure is continuous and
proportional instead of a wall you hit at commit time. The check the
list rates highest — *this commit touched route X's code but not X's
spec* — is deliberately soft here, because it is the check most likely
to be wrong (a pure refactor, a shared module), and a blocker that is
sometimes wrong is what teaches people to bypass the gate. Its escape
hatch is a commit trailer (`Spec-exempt: refactor`), not a label, so
the judgement lands in the history where the next audit reads.

Reading it back needs no new mechanism: STATUS.md's frontmatter
(`audited_at`, `unaudited_commits`, `freshness`) is what the cockpit
renders, the same way it already reads cmd and rst frontmatter.

## Three smaller ones

**Commit messages.** Drift is measured with git dates, which makes the
commit message part of the system rather than a style preference — the
smallest spec there is. The kit prescribes prefixes (`hotfix:`, `mf:`)
and says nothing about content. Add: subject ≤ 72 chars saying what
changes in *behavior*, not which file was touched; name the route or
mechanism; `fix` / `wip` / `update` are not complete messages; optional
trailer `Spec: pages/orders/[id].md`, which makes the pairing
machine-checkable and feeds the soft check above.

**`docs/` root closed.** An allowlist (`README.md`, `CLAUDE.md`, the
fixed REQ documents); everything else picks a genre. Worth stating only
once the five genres exist — a closed root with nowhere to put things
is friction without a payoff. Soft check.

**`HANDOVER.md`.** The kit has `KIT.md` (for whoever maintains the kit)
and `CLAUDE-section.md` (for the AI). Nothing addresses a human
successor: which commands exist, what cadence, what to run once in week
one. For MagnaFlow this barely matters — our successor is a fresh AI
session reading CLAUDE.md. For a client project it is the whole point,
and the evaluation's sharpest finding is that commands are inherited
and habits are not.

## Order of work

1. `docs/decisions/` and `docs/context/`: folders, templates,
   README-as-house, adopt/update creating both.
2. `/decision <slug>` — next number off disk, fill the template, write
   the index line, add the `Why:` line to the owning concept.
3. Commit-message section, `docs/` root allowlist, `HANDOVER.md`.
4. `spec_lint` in the kit with the hard/soft split; worker hook; the
   cockpit's per-project spec signal.

1–3 are documentation-only migrations for the kit. 4 touches the worker
and the cockpit and needs a prompt pair of its own.

Already shipped in kit 0.12: the positive concept threshold, and the
`/spec-drift` signal fixes (cosmetic diffs counted not listed,
audit-state frontmatter, `ACCEPTED.md`). Migration notes in
`tools/mf-spec/spec-kit/KIT.md`.
