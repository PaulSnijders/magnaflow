# mf-worker — MagnaFlow Worker Controller (v0.2)

One-shot controller that executes AI-delegated commands in a target project.
It picks up a `ready` command from `docs/prompts/`, has an AI coding agent plan
and implement it on a work branch, runs the project's build and tests with
bounded retries, and writes status, logs and report back as committed
plain-text files. Then it stops: no polling, no daemon.

Behavior specs: [docs/specs/worker/](../../docs/specs/worker/) and the
concepts they link, chiefly [worker run](../../docs/specs/concepts/worker-run.md).
The original Spec Kit contracts (001, 002) are history:
`git show c0e1353:specs/`. Hands-on walkthrough with an example project:
[EXAMPLES.md](EXAMPLES.md).

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
target project root containing `docs/prompts/` and `.magnaflow/`.

## The plan/questions/report cycle

Every run of a `ready` command starts with a plan phase: the agent reads the
command body, its linked specs and the project's conventions (below) before
touching code.

- **Nothing open** → the run continues straight into implementation, in the
  same agent session. No plan file is written.
- **A genuine question** (one only a human can decide) → the run pauses. The
  agent writes `NNNN-pln-name.md` with its plan and questions, the question
  goes into `NNNN-qa-name.md`, and the status becomes `questions`. So a pln in
  the lane means exactly one thing: this command needed a human. Answer
  beneath the question and set `status:` back to `ready`. The next run resumes
  the *same* agent session and re-plans, rewriting that pln without the
  questions.
- **A terminal outcome** (`done` or `aborted`) always leaves a report,
  `NNNN-rst-name.md`: what was done, decisions taken, open questions the agent
  answered itself, and for `aborted` the reason (retries exhausted, human
  abandonment, or an unrecoverable environment error).

The controller is optional: every artifact (`cmd`/`pln`/`qa`/`rst`) and every
status change is a plain-text file a human can read and edit directly. The
manual path stays first-class.

## Target project layout

```text
my-project/
├── docs/
│   └── prompts/
│       ├── 0001-cmd-add-footer.md   # the command: goal, context, acceptance criteria
│       ├── 0001-pln-add-footer.md   # only when the plan raised a question for you
│       ├── 0001-qa-add-footer.md    # only if a genuine question arose
│       └── 0001-rst-add-footer.md   # the report (only once a terminal outcome is reached)
└── .magnaflow/
    ├── config.yml                  # build/test commands (+ optional defaults, agent)
    └── 0001-add-footer/
        ├── claude.log              # raw agent output, plan phase + every attempt
        ├── build.log
        ├── test.log
        └── session.yml             # the agent session ID this command last ended with
```

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
```

`build` and `test` each take exactly one of `command:` (a string) or
`commands:` (a list, run in order; the first failure stops it and its output
is the feedback). A monorepo with two stacks gets one `commands:` entry each:
see [config.yml recipes](EXAMPLES.md#7-configyml-recipes).

The agent runs with its own safe defaults unless the project opts into more
autonomy via `agent.args`. Command file format: contracts/file-formats.md
(`git show c0e1353:specs/002-plan-questions-feedback/contracts/file-formats.md`).

## Project conventions

If the target project has a root `CLAUDE.md` and/or a constitution file
(`.specify/memory/constitution.md`, else `docs/constitution.md`), their
content goes into every phase's agent instructions automatically. No need to
list them under `specs:`. Neither file is required.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Requested work succeeded, or a legitimate pause occurred (command done / paused at `questions` / batch done / status shown / nothing ready) |
| 1 | Command(s) ended `aborted` (retries exhausted, human abandonment, or an unrecoverable environment error) |
| 2 | Usage or configuration error (unknown command, invalid config, missing spec/base branch) |
| 3 | Precondition refusal — nothing mutated (dirty working tree, command not `ready`) |
| 4 | Environment error (git or agent executable unavailable) |

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
  command ID (`0001-hello-page`) means that command's recorded session; any
  other value is a raw agent session ID, passed through verbatim.

`resume:` and a matching `group:` both beat the default self-resume.
`resume:` together with `fresh_session: true` is an error.

```yaml
resume: 0002-add-styling            # continue the context command 0002 ended with
# or:
resume: 6a1f0e6e-9c1d-4f5a-b0e2-…   # continue a specific session directly
```

## Git branches

A run commits on two branches:

- **Invoking branch**: the branch you start mf-worker from (your
  *orchestration branch*, normally `main`). It gets all bookkeeping: the
  `running` commit, the command file, its plan/report and the `.magnaflow/`
  evidence, ending in the `questions`/`done`/`aborted` commit. `status`,
  `next` and `run-all` always read the queue from the checked-out branch.
- **Work branch**: the command's `branch:`, created from its `base:` (default:
  the repository's default branch). It is created only once planning passes,
  never for a run that pauses or fails while planning. It gets only the
  agent's code changes: a clean diff for review. Nothing is merged
  automatically; you review and merge it yourself.

```text
main            o── running ── planned ─────────────────── done (report + evidence) ──►
                                  \                        /
task/0001-x                        o── agent's work commit ──o   (only src/tests changes)
```

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
Good for solo projects or low-risk commands where a review branch is overhead.
`base:` requires `branch:`; a command with a base but no branch is rejected as
malformed.
