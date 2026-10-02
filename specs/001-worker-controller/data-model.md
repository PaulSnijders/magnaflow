# Data Model: MagnaFlow Worker Controller (v0.1)

All entities are plain-text files in the target project (constitution II); the C# types below
are in-memory projections of those files, never a separate store.

## Task

One unit of delegated work = one folder `.magnaflow/tasks/NNNN-short-name/`.

| Field (frontmatter) | Type | Required | Rules |
|---------------------|------|----------|-------|
| *(folder name)* | string | yes | Task ID and ordering key; pattern `^\d{4}-[a-z0-9-]+$`; never duplicated inside the file (FR-001) |
| `title` | string | yes | Human-readable short title |
| `status` | enum | yes | `pending \| running \| done \| failed`; unknown value ⇒ task reported malformed and skipped |
| `branch` | string | no | Work branch name (e.g., `task/0001-short-name`). Absent = branchless mode: work is committed directly on the invoking branch (FR-006). The controller refuses to run while any task's work branch is checked out (FR-006a) |
| `base` | string | no | Branch to create `branch` from; default: repository default branch; must exist; only valid together with `branch` (FR-006) |
| `group` | string | no | Session-continuity key across consecutive tasks (FR-013) |
| `fresh_session` | bool | no (default `false`) | `true` forces a new agent session even within a matching group; not combinable with `resume` |
| `resume` | string | no | Explicit session continuation (FR-013a). Task-ID pattern ⇒ continue that task's recorded session from its result.yml; any other value ⇒ raw agent session ID, verbatim. Takes precedence over group continuity |
| `specs` | string[] | no (default empty) | Repo-relative paths whose full content joins the prompt; any missing path aborts preparation (FR-007) |
| `attempts` | int | yes (default 0) | Attempts used; controller-owned. Tracked in memory during the run, written to the file together with the terminal status (research R6 invariant) |
| `max_attempts` | int | no | Overrides `defaults.max_attempts` from config |
| `created` | date | no | Informational only |

**Body** (Markdown below the frontmatter): `## Goal`, `## Context`, `## Acceptance criteria` —
free-form; passed verbatim into the prompt; never machine-verified (Clarification 2026-07-02).

**Write rule**: the controller only ever rewrites the `status:` and `attempts:` values, via
line-targeted replacement inside the frontmatter block (research R4). Everything else in
`task.md` is user-owned.

## TaskStatus (state machine)

```text
pending ──run──> running ──build+tests pass──> done      (terminal)
                    └──────max_attempts exhausted──> failed  (terminal)
```

- Only `pending` tasks are runnable (FR-004); `running` without a live controller = stuck,
  human resets by editing the file (spec edge case).
- Every transition is committed on the orchestration plane (research R6): one commit for
  `pending→running`, one for `running→done|failed` (bundled with logs + result.yml).

## ProjectConfig

`.magnaflow/config.yml` — the contract between a target project and any controller.

| Field | Type | Required | Default | Rules |
|-------|------|----------|---------|-------|
| `build.command` | string | one of `command`/`commands` | — | Shell command; neither present ⇒ refuse to run tasks (exit 2) |
| `build.commands` | string[] | one of `command`/`commands` | — | Run sequentially; first failure stops the sequence (its output is the feedback) |
| `test.command` | string | one of `command`/`commands` | — | Shell command; neither present ⇒ refuse to run tasks (exit 2) |
| `test.commands` | string[] | one of `command`/`commands` | — | Run sequentially; first failure stops the sequence (its output is the feedback) |
| `defaults.max_attempts` | int | no | 3 | Used when task has no `max_attempts` |
| `defaults.command_timeout_minutes` | int | no | 30 | Per external command (agent, build, test) |
| `agent.command` | string | no | `claude` | The replaceable executor (FR-019) |
| `agent.args` | string[] | no | `[]` | Passed verbatim; where `--dangerously-skip-permissions` is opted into (FR-008a) |

## ExecutionResult

`result.yml` per task folder; written fresh by the controller at the end of every run
(FR-011).

| Field | Type | Rules |
|-------|------|-------|
| `task` | string | Folder name (task ID) |
| `status` | enum | `done \| failed` |
| `attempts` | int | Attempts actually used (≥1) |
| `started` / `finished` | ISO-8601 local datetime | Wall-clock bounds of the run |
| `session` | string? | Agent session ID the run ended with (the chain tail after any resumes); absent when the agent never reported one. Target of other tasks' `resume:` |
| `summary` | string (paragraph) | One paragraph: what was built/changed and why; taken from the agent's final response, else controller-generated fallback |

## Execution Logs

Per task folder, controller-written, appended per attempt with an attempt-header line:

- `claude.log` — every raw output line from the agent process (stream-json lines, verbatim)
- `build.log` — build command stdout+stderr
- `test.log` — test command stdout+stderr

Untracked while the run is in flight; committed with the terminal status (research R6).

## AgentSession (in-memory only — deliberately not persisted)

| Field | Type | Notes |
|-------|------|-------|
| `SessionId` | string | From agent init event (research R5) |
| `Group` | string? | The `group:` of the task that opened the session |

Continuation rules (FR-012/013/013a), in order of precedence:

1. `resume:` in the frontmatter (explicit; works across invocations because the session ID
   comes from a file — a raw ID or another task's result.yml).
2. Retries within a task always resume the running `SessionId`.
3. The next task in the same invocation resumes it only if its `group` equals `Group`,
   it directly follows, and `fresh_session` is not set.

The in-memory state still dies with the controller process, but every run records its final
session ID in result.yml, so any later task can pick the thread back up via `resume:`.

## Relationships

```text
ProjectConfig 1 ── * Task              (config governs every run)
Task 1 ── 0..1 ExecutionResult         (absent = never fully run)
Task 1 ── 0..3 Log files               (absent = never run / phase not reached)
Task * ── 0..1 AgentSession            (transient, via group)
Task 0..1 ── base ──> git branch       (work-branch creation point)
```
