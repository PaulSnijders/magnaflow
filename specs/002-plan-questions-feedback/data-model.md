# Data Model: Worker Controller — Plan, Questions & Feedback (v0.2)

All entities are plain-text files (constitution II); the C# types below are in-memory projections,
never a separate store. This amends `specs/001-worker-controller/data-model.md`: `Task`/`TaskFile`
becomes `Command`/`CmdFile`, `.magnaflow/tasks/NNNN-name/task.md` becomes
`docs/prompts/NNNN-cmd-name.md`, and `result.yml` is retired (research R6).

## Command (cmd)

One unit of delegated work = `docs/prompts/NNNN-cmd-name.md`. `NNNN-name` is shared, verbatim, by
its optional sibling files (Plan, Question/Answer, Report below).

| Field (frontmatter) | Type | Required | Rules |
|---------------------|------|----------|-------|
| *(filename)* | string | yes | `NNNN-cmd-name.md`; `NNNN` + `name` together are the command's unique ID and ordering key; never duplicated inside the file (FR-001) |
| `title` | string | yes | Human-readable short title |
| `status` | enum | yes | `draft \| ready \| running \| questions \| done \| aborted`; unknown value ⇒ malformed, reported and skipped |
| `branch` | string | no | Work branch name; absent = branchless mode (unchanged from v0.1 FR-006) |
| `base` | string | no | Branch to create `branch` from; only valid together with `branch` (unchanged from v0.1) |
| `group` | string | no | Session-continuity key across consecutive commands in one invocation (unchanged from v0.1 FR-013) |
| `fresh_session` | bool | no (default `false`) | Forces a new session even within a matching group; not combinable with `resume` |
| `resume` | string | no | Explicit session continuation (unchanged from v0.1 FR-013a); takes precedence over both group continuity and the new default self-resume (FR-013) |
| `specs` | string[] | no (default empty) | Repo-relative paths whose full content joins every phase's prompt; any missing path aborts preparation (unchanged from v0.1 FR-007) |
| `attempts` | int | yes (default 0) | Attempts used; incremented only by agent crashes and build/test repair retries — never by a clean pause (FR-011a) |
| `max_attempts` | int | no | Overrides `defaults.max_attempts` from `.magnaflow/config.yml` |
| `created` | date | no | Informational only |

**Body**: `## Goal`, `## Context`, `## Acceptance criteria` — free-form, passed verbatim into every
phase's prompt; never written by the executor (FR-005).

**Write rule**: the controller only ever rewrites `status:` and `attempts:`, in place (unchanged
mechanism from v0.1, now applied to the cmd file). The human may also hand-edit `status:` directly
(e.g., `draft → ready`, `questions → ready`, or manually to `aborted`) — this is not
controller-exclusive (FR-022).

## CmdStatus (state machine)

```text
draft ──(human, out of scope for the controller)──> ready
ready ──run──> running ──plan: no open questions──> (execution) ──build+tests pass──> done   (terminal)
                  │                                                └─exhausted/crash/abandoned─> aborted (terminal)
                  └─plan: open question(s)──> questions ──human answers + resets status──> ready
```

- Only `ready` commands are runnable (FR-006); any other status refuses execution unchanged, no
  special case for resuming `questions` directly.
- `draft` is human-authoring-only; the controller never transitions into or out of it.
- `questions` is non-terminal and revisitable an unbounded number of times (FR-011a, FR-015); it
  requires the human's own edit (answer + status reset) before the controller acts again.
- `aborted` is a single terminal status for every non-success reason — retries exhausted, human
  abandonment, or an unrecoverable environment error (FR-018) — distinguished only by the reason
  recorded in the rst.

## Plan (pln)

`docs/prompts/NNNN-pln-name.md` — executor-owned (FR-009). Written by the plan phase **only** when
the round has an open question, or when a pln/qa for this command already exists and an earlier
round's open-questions section has to be dropped; rewritten **in place** on re-plan (not
duplicated per attempt, research R3). A well-specified command has no pln at all — the plan itself
is produced in the plan step's reply and stays in the agent session that implements from it.

| Section | Content |
|---------|---------|
| Plan | Enough of the agent's intended approach to make the open question concrete |
| Open questions (this round) | Mirrors what was just appended to the qa file this run; the exact heading the controller reads at the gate |

No frontmatter contract is mandated beyond it being Markdown; the controller does not parse pln
content, only writes/overwrites it via the agent's own output (research R5) and checks for its
existence (FR-001/FR-009).

## Question/Answer dialogue (qa)

`docs/prompts/NNNN-qa-name.md` — created only when the plan phase raises a genuine open question
(FR-011). Structure: one entry per question, appended chronologically; the human writes the answer
directly beneath the relevant question (FR-012). Further pause rounds append further
question/answer entries to the **same** file (FR-015) — never a new qa file per round.

The controller's own signal that a run should pause is the presence, after the plan-phase agent
invocation, of a question in this file with no answer beneath it (research R5) — not a
machine-readable flag.

## Report (rst)

`docs/prompts/NNNN-rst-name.md` — executor-owned; written or updated **only** when a run's
execution phase reaches a terminal outcome (`done` or `aborted`), never for a run that ends in
`questions` (FR-019).

| Content | Rules |
|---------|-------|
| What was done | Prose account of the actual work performed this run, reflecting the final state after any retries (FR-017) |
| Decisions taken while implementing | What was decided or changed along the way, and anything skipped or left uncertain — the rst MUST NOT restate the plan (FR-017) |
| Self-answered questions | Each question the cmd left open that the executor resolved itself, with the answer it settled on (FR-017) |
| Abort reason (when `aborted`) | The specific reason: retries exhausted, human abandonment, or unrecoverable environment error (FR-018) |

## SessionEvidence

`.magnaflow/<NNNN-name>/session.yml` — the sole successor to v0.1's `result.yml` for cross-run
session continuity (research R4/R6). Controller-owned; written whenever a run ends (paused or
terminal) with a known agent session ID.

```yaml
session: 6a1f0e6e-...
```

Read by `ResumeResolver`'s default self-resume path (FR-013) whenever no explicit `resume:` or
matching `group` continuity applies. Absent file ⇒ no prior session ⇒ start fresh.

## Machine Runtime Evidence (`.magnaflow/<NNNN-name>/`)

Per-command, controller-written, append-per-attempt with an attempt-header line (unchanged
mechanism from v0.1's log files, now scoped under the command's own evidence folder instead of a
`.magnaflow/tasks/<id>/` task folder):

- `claude.log` — every raw agent output line, both the plan-phase invocation and the execution
  phase's attempts, each with its own header (`=== plan ===`, `=== attempt 2/3 ===`)
- `build.log`, `test.log` — unchanged from v0.1
- `session.yml` — see above

`.magnaflow/` MUST hold nothing else for a command: no status, no summary, no bookkeeping content
(FR-004) — that is exclusively `docs/prompts/`'s job now.

## ProjectConfig

`.magnaflow/config.yml` — unchanged from v0.1 (`specs/001-worker-controller/data-model.md`): build
command, test command, retry/timeout defaults, agent command/args. Not part of this feature's
scope beyond the fixed convention-file lookup (below), which needs no new config field.

## Project Conventions

Not a file the controller owns — read-only input, discovered via a fixed order (FR-020), no
config field:

1. `CLAUDE.md` at the repository root.
2. Else, a constitution file: `.specify/memory/constitution.md`, then `docs/constitution.md`
   (first hit wins).

Either or both absent ⇒ silently proceed without them (FR-021). Content, when found, is prepended
to every phase's agent prompt (plan and execution alike).

## Relationships

```text
ProjectConfig 1 ── * Command                    (config governs every run)
Command 1 ── 0..1 Plan                          (absent = plan phase never ran, or command not yet run)
Command 1 ── 0..1 Question/Answer dialogue      (absent = never paused)
Command 1 ── 0..1 Report                        (absent = execution phase never reached a terminal outcome)
Command 1 ── 0..1 SessionEvidence               (absent = no session ever recorded)
Command 1 ── 0..3 Log files                     (absent = never run / phase not reached)
Command 0..1 ── base ──> git branch             (work-branch creation point, unchanged from v0.1)
Project Conventions * ── * Command              (read-only, included in every phase's prompt)
```
