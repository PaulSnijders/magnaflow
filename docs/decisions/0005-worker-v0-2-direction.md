---
date: 2026-07-07
topic: worker
status: accepted
---

# 0005 — Worker Controller v0.2 — direction (2026-07-07)

> Migrated from `docs/decisions/0005-worker-v0-2-direction.md` on 2026-10-05. Pre-dates the four-section
> decision format; original text unchanged.

With Spec Kit retired from the default workflow, the Worker Controller
becomes relevant again as the execution half of the original loop:

```text
talk in Cowork (docs/ only, high level)      ← thinking plane
      → state specs updated, task written
      → .magnaflow/tasks/NNNN-name/
      → mf-worker runs the task              ← execution plane
      → summary + result back
      → next conversation grounded in it
```

The separation of planes is deliberate, not an accident of tooling:
talking against `docs/` only keeps the conversation high-level and free
of irrelevant code detail — and if a detail *does* matter, that is the
signal it belongs in a spec. v0.1 is built and works; these are the
v0.2 requirements from practice.

## 1. Plan step with questions

Before executing, the worker runs a planning pass over the task + linked
specs and produces a short plan plus **concrete questions**. Then the
same gate as everywhere else in MagnaFlow (see `docs/context/2026-07-07-cockpit-multi-project-idea.md`):

- Questions the agent can answer itself from specs/code/conventions →
  answer them, note them in the plan, continue.
- Questions that genuinely need the human → stop, surface them, wait.

Sketch (not pinned): plan and questions land in the task folder
(`plan.md`, questions in `result.yml` or frontmatter status
`questions` — same vocabulary as the prompt lane in `docs/prompts/`);
the human answers by editing the task file — plain text, git-tracked,
like everything else.

**CLI mechanics (verified against the Claude Code docs, 2026-07):**
headless `claude -p` is strictly non-interactive — an agent cannot pause
to ask; an unapproved permission prompt aborts the run. So the pattern
is stop-and-resume:

1. The controller instructs the agent: "if blocked on a decision that
   is genuinely the human's, do not guess — stop and return your
   questions." With `--output-format json --json-schema` the reply is
   forced into a contract, e.g.
   `{outcome: done|questions, questions: [], summary}` — no output
   parsing, and the `session_id` comes back in the same JSON.
2. On `questions`: controller writes them to the task folder, sets
   frontmatter status `questions`, exits (one-shot).
3. Human edits the task file with answers; on the next run the
   controller resumes the SAME session (`--resume <session_id>`,
   already stored in result.yml today) so no context is lost. Resume is
   scoped to the same project directory — fine, the worker always runs
   from the repo root.
4. Permissions must be pre-approved (`--permission-mode` /
   `--allowedTools` / `--dangerously-skip-permissions` from config.yml
   `agent.args`) — a denied tool does not ask, it kills the run.

## 2. Feedback that feeds the next conversation

The controller already writes `result.yml` with a summary; v0.2 should
make the feedback loop first-class:

- A readable per-task report (what was done, decisions taken while
  implementing, anything skipped or uncertain) — written for the next
  Cowork session to read, not just for logging.
- Enough detail that follow-up conversation can continue from the
  report instead of re-reading the diff.

## 3. Clearer instruction of the worker

The prompt build (task body + linked specs) works; add the project's
constitution/conventions so the worker inherits the same guardrails a
human session has via CLAUDE.md.

Not in scope yet: dispatcher, parallel workers — unchanged from v0.1's
"deliberately not" list.

## Invariant: the controller stays optional

The manual path — copy a prompt into Claude Code by hand — remains
first-class, permanently. The controller is an executor of the same
plain-text formats, never a gatekeeper: nothing (plan files, questions,
reports, statuses) may become readable or writable only through the
controller. If a v0.2 mechanism can't also be done by a human editing
files, the mechanism is wrong.
