# mf-worker — MagnaFlow Worker Controller

One-shot controller that executes AI-delegated commands in a target project.
It picks up a `ready` command from `docs/prompts/`, has an AI coding agent plan
and implement it, runs the project's build and tests with bounded retries, and
commits the outcome (status and report) as plain-text files. Then it stops:
no polling, no daemon.

Behavior specs: [docs/specs/worker/](../../docs/specs/worker/) and the
concepts they link, chiefly [worker run](../../docs/specs/concepts/worker-run.md).
The specs own the details; this README is a summary. Hands-on walkthrough with
an example project: [EXAMPLES.md](EXAMPLES.md).

## Build

```powershell
cd tools\worker-controller
dotnet build
dotnet test
```

Requires the .NET 10 SDK, git, and an AI agent CLI (default: Claude Code,
authenticated).

## Commands

```text
mf-worker run <cmd-id>    # execute one specific command end-to-end
mf-worker next            # execute the first ready command (ID order)
mf-worker run-all         # drain all ready commands one by one, then stop
mf-worker status          # read-only command overview (never writes)
```

All commands accept `--project <path>` (default: current directory): a
target project root containing `docs/prompts/` and `.magnaflow/config.yml`.

## The plan/questions/report cycle

Every run of a `ready` command starts with a plan phase: the agent reads the
command body, its linked specs and the project's conventions (below) before
touching code.

- **Nothing open** → the run continues straight into implementation, in the
  same agent session. No plan file is written.
- **A genuine question** (one only a human can decide) → the run pauses. The
  agent writes `NNNN-pln-name.md` with its plan and questions, the questions
  go into `NNNN-qa-name.md`, and the status becomes `questions`. So a pln in
  the lane means exactly one thing: this command needed a human. Answer
  beneath each question, set `status:` back to `ready` and commit. The next
  run resumes the *same* agent session and re-plans, rewriting that pln in
  place.
- **A terminal outcome** (`done` or `aborted`) always leaves a report,
  `NNNN-rst-name.md`, with a one-line `summary:`: what was done, decisions
  taken, questions the agent answered itself, and for `aborted` the reason
  (the last failing phase).

The controller is optional: every artifact (`cmd`/`pln`/`qa`/`rst`) and every
status change is a plain-text file a human can read and edit directly. The
manual path stays first-class. File formats, statuses and who may move them:
[command lifecycle](../../docs/specs/concepts/command-lifecycle.md).

## Target project layout

```text
my-project/
├── .gitignore                       # .magnaflow/*  and  !.magnaflow/config.yml
├── docs/
│   └── prompts/
│       ├── 0001-cmd-add-footer.md   # the command: goal, context, acceptance criteria
│       ├── 0001-pln-add-footer.md   # only when the plan raised a question for you
│       ├── 0001-qa-add-footer.md    # only if a genuine question arose
│       └── 0001-rst-add-footer.md   # the report (only once a terminal outcome is reached)
└── .magnaflow/
    ├── config.yml                   # committed: build/test commands (+ optional sections)
    └── 0001-add-footer/             # machine-local evidence, gitignored
        ├── claude.log               # raw agent output, plan phase + every attempt
        ├── build.log
        ├── test.log
        └── session.yml              # the agent session ID this command last ended with
```

Everything in `.magnaflow/` except `config.yml` is machine-local and should be
gitignored as shown. A project that still tracks it gets its evidence
committed with each outcome. Details:
[evidence layout](../../docs/specs/concepts/evidence-layout.md).

`.magnaflow/config.yml`:

```yaml
build:
  command: dotnet build          # required unless commands: is given
test:
  command: dotnet test           # required unless commands: is given
defaults:
  max_attempts: 3                # optional
  command_timeout_minutes: 30    # optional
agent:
  command: claude                # optional; the agent is swappable
  args: []                       # optional; e.g. [--dangerously-skip-permissions]
run:                             # optional; see "mf-run around the run" below
  command: mf-run
  services: [...]                # mf-run's service list
```

`build` and `test` each take exactly one of `command:` (a string) or
`commands:` (a list, run in order; the first failure stops it and its output
is the feedback). A monorepo with two stacks gets one `commands:` entry each:
see [config.yml recipes](EXAMPLES.md#7-configyml-recipes). Full reference:
[machine and project config](../../docs/specs/concepts/machine-config.md#project-magnaflowconfigyml).

The agent runs with its own safe defaults unless the project opts into more
autonomy via `agent.args`.

## Project conventions

If the target project has a root `CLAUDE.md` and/or a constitution file
(`.specify/memory/constitution.md`, else `docs/constitution.md`), their
content goes into every phase's agent instructions automatically. No need to
list them under `specs:`. Neither file is required.

## Exit codes

The specs own these: [`run`](../../docs/specs/worker/run.md#exit-codes),
[`next`](../../docs/specs/worker/next.md#exit-codes),
[`run-all`](../../docs/specs/worker/run-all.md#exit-codes),
[`status`](../../docs/specs/worker/status.md#exit-codes). In short:

| Code | Meaning |
|------|---------|
| 0 | `done`, paused at `questions`, nothing ready, batch finished, or status shown |
| 1 | the command ended `aborted` (`run-all`: at least one did) |
| 2 | usage or config error: bad arguments, config missing or invalid, unknown id, malformed command, missing `specs:` file, unresolvable `resume:`, no base branch |
| 3 | refused, nothing changed: dirty tree, on a work branch, command not `ready`, or `mf-run stop` failed (status reverted to `ready` and committed) |
| 4 | environment: git or agent unavailable, detached HEAD, or a git failure mid-run |

`run-all` keeps going past 1 and 2, but stops at once on 3 or 4. `status`
always exits 0.

## Agent sessions

Context carries over through files, never implicitly. Full rules:
[session resume](../../docs/specs/concepts/session-resume.md).

- **Within a run**: plan phase, implementation and every retry share one
  session, so the agent keeps its context while fixing build/test failures.
- **Across a pause**: the next run resumes the session that paused
  (`.magnaflow/<id>/session.yml`). Nothing to restate.
- **Within one `run-all`**: consecutive commands with the same `group:` share
  a session.
- **Across invocations**: a command's `resume:` continues earlier context. A
  command ID (`0001-hello-page`) means that command's recorded session on this
  machine; any other value is a raw agent session ID, passed through verbatim.

`resume:` and a matching `group:` both beat the default self-resume.
`resume:` together with `fresh_session: true` is an error.

```yaml
resume: 0002-add-styling            # continue the context command 0002 ended with
# or:
resume: 6a1f0e6e-9c1d-4f5a-b0e2-…   # continue a specific session directly
```

## Git branches

A run with a `branch:` commits on two branches:

- **Invoking branch**: the branch you start mf-worker from (your
  *orchestration branch*, normally `main`). It gets all bookkeeping: the
  `running` commit, then the outcome commit with the command file, its
  pln/qa/rst and the final status (`questions`, `done` or `aborted`).
  `status`, `next` and `run-all` always read the queue from the checked-out
  branch.
- **Work branch**: the command's `branch:`, created from its `base:` (default:
  the repository's default branch). It is created only once planning passes,
  never for a run that pauses or fails while planning. It gets only the
  agent's changes (code and specs, never `docs/prompts/` or `.magnaflow/`): a
  clean diff for review. Nothing is merged automatically; you review and merge
  it yourself.

```text
main         o── -> running ──────────────────────── -> done (cmd + rst) ──►
                       \
task/0001-x             o── command 0001-x: <title>   (only the agent's changes)
```

When a remote exists, the work branch is pushed after its commit and the
invoking branch after each outcome commit. A failed push is only a warning.
The worker never opens a pull request. Step-by-step order:
[worker run](../../docs/specs/concepts/worker-run.md#the-run).

**A work branch is a frozen snapshot.** It branches off mid-run, so its
bookkeeping files are frozen at that moment; the live queue is on your
orchestration branch. So mf-worker **refuses to run from a work branch**
(exit 3, printing the checkout command to fix it), and `status` warns there.
Review work branches without switching:

```powershell
git diff main...task/0001-hello-page    # what the command changed
git show task/0001-hello-page           # the work commit
git merge task/0001-hello-page          # accept it (from main)
```

If you check one out to try the result, switch back to `main` before running
mf-worker again.

**Running without branches.** Omit `branch:` and the command runs
*branchless*: the whole run lands on the invoking branch as a **single
commit**. The claim commit (`-> running`) is amended at the end to carry the
work, the report and `-> done`, as `command <id>: <title>`. If amending is
unsafe (the agent committed itself, so HEAD moved, or the claim commit already
reached a remote), the run falls back to a work commit plus a status commit.
A pause is always two commits (claim plus `-> questions`). Good for solo
projects or low-risk commands where a review branch is overhead. `base:`
requires `branch:`; a command with a base but no branch is rejected as
malformed.

**Aborted work is committed too**, on the work branch or (branchless) on the
invoking branch, so a human can inspect it. Nothing is discarded.

## mf-run around the run

Only when `.magnaflow/config.yml` has `run.services`. The worker runs
`mf-run stop` right before implementation (after planning, so the app keeps
running through a pause) and `mf-run start` after `done` or `aborted`. A
failed stop refuses the run (exit 3, status back to `ready`); a failed start
is only a warning in the rst. Details:
[worker run](../../docs/specs/concepts/worker-run.md#mf-run-around-the-run).
