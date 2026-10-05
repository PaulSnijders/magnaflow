# Technical

Which agent session a worker run continues. Sessions keep context: a plan
and its implementation, a retry and its failure, a paused command and
its answer, a follow-up and its parent. The worker never interprets a
session id, it only passes it to `--resume`.

## Precedence

Decided once at the start of a run. The first rule that applies wins:

1. **`resume:`** in the cmd frontmatter, resolved as below.
2. **`group:`**: the same group as the command run immediately before,
   in the **same invocation** (`run-all`), continues that command's final
   session. `fresh_session: true` skips only this rule.
3. **Self-resume**: this command's own `.magnaflow/<id>/session.yml` from
   an earlier run, typically the one that paused at `questions`.
4. Otherwise a fresh session.

Within a run, the plan phase, every implementation attempt and every
failure-feedback prompt continue the session the previous step returned.
`resume:` together with `fresh_session: true` makes the command
malformed. The group hand-over is in memory only and never crosses
invocations, so `run` and `next` never use it.

Note that self-resume also applies with `fresh_session: true`, and to an
`aborted` command that is re-readied: it continues its old session.

## Resolving `resume:`

Two forms, told apart by shape:

- **Id-shaped** (`^\d{4}[B-Z]?-[a-z0-9-]+$`, after normalization) is a
  command reference. It resolves to that command's recorded `session.yml`,
  so a lineage survives re-recording.
- **Anything else** is a raw agent session id, passed through verbatim.
  It is not checked. The cockpit's follow-up writes this form.

`ResumeResolver` tries candidates in order:

1. The value verbatim, if it is id-shaped. A `.magnaflow/<value>/` match
   wins, so a slug that really starts with a lane word resolves to
   itself.
2. The value normalized: directory part and `.md` dropped, then a
   `cmd|pln|qa|rst` segment stripped **only** directly after the number.
   `docs/prompts/0008B-rst-funnel.md` becomes `0008B-funnel`, while
   `0012-fix-qa-export` stays itself.

| Outcome | Result |
|---|---|
| no candidate is id-shaped | raw session id, verbatim |
| a candidate's `.magnaflow/<id>/` exists with a session | that session |
| the folder exists, no session recorded | exit 2: "has no recorded session yet" |
| no candidate's folder exists | exit 2: names every id tried and lists the commands that have run |

An unresolvable reference is a hard error, never a silent fresh session.
Quietly losing the parent's context is worse than stopping, and the
message makes the fix a one-line edit.

## Machine-local

`session.yml` is gitignored ([evidence layout](evidence-layout.md#why-machine-local)).
The agent resolves a session against transcripts stored per machine and
per checkout path. A lineage therefore continues on the machine that ran
it. Elsewhere a command reference fails (no folder, exit 2), and a raw
id is handed to the agent, which decides what happens. A follow-up
created in the cockpit for a parent without a recorded session gets no
`resume:` and a "will start cold" warning: the fresh-start fallback is
explicit at creation time, never at run time.

DRAFT: generated from code, not human-reviewed.

Code: tools/worker-controller/src/MagnaFlow.WorkerController/Execution/ResumeResolver.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Execution/SessionEvidence.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Execution/TaskRunner.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Execution/RunAllExecutor.cs, tools/mf-cockpit/src/MagnaFlow.MfCockpit/Prompts/DraftWriter.cs
Why: prompts/0006-cmd-resume-id-resolution.md
