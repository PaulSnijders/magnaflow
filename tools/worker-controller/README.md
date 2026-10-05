# mf-worker — MagnaFlow Worker Controller (v0.2)

One-shot controller that executes AI-delegated commands in a target project: it picks up a
`ready` command from `docs/prompts/`, has an AI coding agent plan it, implement it on a work
branch, run the project's build and tests with bounded retries, and write status, logs, plan,
and report back as committed plain-text files. It does its job and stops — no polling, no daemon.

Behavior specs: [docs/specs/worker/](../../docs/specs/worker/) and the concepts they link. The
original Spec Kit contracts (001, 002) are history: `git show c0e1353:specs/`. Hands-on walkthrough with a ready-made example
project: [EXAMPLES.md](EXAMPLES.md).

## Build

```powershell
cd tools\worker-controller
dotnet build
dotnet test
```

Requires the .NET 10 SDK, git, and an AI agent CLI (default: Claude Code, authenticated).

## Commands

```text
mf-worker run <cmd-id>    # execute one specific command end-to-end
mf-worker next            # execute the first ready command (ID order)
mf-worker run-all         # drain all ready commands one by one, then stop
mf-worker status          # read-only command overview (never writes)
```

All commands accept `--project <path>` (default: current directory) pointing at a target
project root containing `docs/prompts/` and `.magnaflow/`.

## The plan/questions/report cycle

Every run of a `ready` command first has the agent plan: a separate invocation over the command
body, its linked specs, and the project's own conventions (below), before any code is touched.

- **Nothing genuinely open** → the controller continues immediately, same agent session, into
  implementation — no added friction for a well-specified command, and no plan file: the plan
  stays in the session that implements from it.
- **A genuine question** (something only a human can decide) → the controller pauses: the agent
  writes `NNNN-pln-name.md` with its plan and the open questions, the question lands in the
  command's `NNNN-qa-name.md`, status becomes `questions`, and the run ends. A pln in the lane
  therefore means exactly one thing: this command needed a human. Answer by writing beneath the
  question and setting `status:` back to `ready`; the next run resumes the *same* agent session
  automatically and re-plans from the answer — rewriting that same pln, without the questions.
- **A terminal outcome** (`done` or `aborted`) always leaves a report in `NNNN-rst-name.md`:
  what was done, the decisions taken while implementing, the questions the command left open that
  the agent answered for itself, and — for `aborted` — the specific reason (retries exhausted,
  human abandonment, or an unrecoverable environment error).

None of this requires the controller: every artifact (`cmd`/`pln`/`qa`/`rst`, and every status
transition) is a plain-text file a human can read, write, or hand-edit directly — the fully
manual, interactive path stays first-class permanently.

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

`build`/`test` each accept `command:` (single string) or `commands:` (a list, run
sequentially — the first failure stops the sequence and its output is the feedback); exactly
one of the two is required per section. A monorepo with two stacks gets one `commands:` entry
each — see [config.yml recipes](EXAMPLES.md#7-configyml-recipes).

The agent runs with its own safe defaults unless a project explicitly opts into more autonomy via
`agent.args`. Command file format:
contracts/file-formats.md (`git show c0e1353:specs/002-plan-questions-feedback/contracts/file-formats.md`).

## Project conventions

If the target project has a `CLAUDE.md` at its root and/or a constitution file (checked at
`.specify/memory/constitution.md`, then `docs/constitution.md`), their content is included
automatically in every phase's agent instructions — no need to list them under `specs:`. Neither
file is required.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Requested work succeeded, or a legitimate pause occurred (command done / paused at `questions` / batch done / status shown / nothing ready) |
| 1 | Command(s) ended `aborted` (retries exhausted, human abandonment, or an unrecoverable environment error) |
| 2 | Usage or configuration error (unknown command, invalid config, missing spec/base branch) |
| 3 | Precondition refusal — nothing mutated (dirty working tree, command not `ready`) |
| 4 | Environment error (git or agent executable unavailable) |

## Agent sessions

Context between commands is opt-in and file-based:

- **Within one command's run**: retries always continue the same agent session, so the agent
  keeps its context while fixing build/test failures — and the plan phase and implementation
  phase of the same run share it too.
- **Across a pause**: by default, the next run of a paused command resumes the exact session that
  paused (`.magnaflow/<id>/session.yml`) — no context lost, nothing to restate.
- **Within one invocation**: consecutive commands with the same `group:` share a session during
  a `run-all`.
- **Across invocations, explicitly**: a command's `resume:` frontmatter continues any earlier
  context — a command ID (`0001-hello-page`) continues that command's recorded session; any other
  value is a raw agent session ID, passed through verbatim. `resume:` and matching `group:` both
  take precedence over the default self-resume; combining `resume:` with `fresh_session: true` is
  an error.

```yaml
resume: 0002-add-styling            # continue the context command 0002 ended with
# or:
resume: 6a1f0e6e-9c1d-4f5a-b0e2-…   # continue a specific session directly
```

## Git branches

A run commits on two planes:

- **Invoking branch** — the branch you start mf-worker from (your *orchestration branch*,
  normally `main`). It carries all bookkeeping: the `running` commit, the command file, its
  plan/report, and the `.magnaflow/` evidence, ending in the `questions`/`done`/`aborted` commit.
  The queue (`status`, `next`, `run-all`) is always read from the currently checked-out branch.
- **Work branch** — the command's `branch:` (created from its `base:`, default: the repository's
  default branch) — created only once planning gates through, never for a run that pauses or
  fails during planning. It gets only the agent's code changes — a clean diff for human review.
  Nothing is merged automatically; you review and merge the branch yourself.

```text
main            o── running ── planned ─────────────────── done (report + evidence) ──►
                                  \                        /
task/0001-x                        o── agent's work commit ──o   (only src/tests changes)
```

**A work branch is a frozen snapshot.** It branches off mid-run, so on that branch the
bookkeeping files are frozen at that moment — by design; the live queue lives on your
orchestration branch. Therefore mf-worker **refuses to run from a work branch** (exit 3, with the
checkout command to fix it), and `status` prints a warning there. Review work branches without
switching:

```powershell
git diff main...task/0001-hello-page    # what the command changed
git show task/0001-hello-page           # the work commit
git merge task/0001-hello-page          # accept it (from main)
```

If you do check one out to try the result, just switch back to `main` before running mf-worker
again.

**Running without branches.** Omit `branch:` from the frontmatter entirely and the command runs
*branchless*: the whole run lands on the invoking branch as a **single commit** — the claim
commit (`-> running`) is amended at the end to carry the work, the report and `-> done`, under
the message `command <id>: <title>`. If amending would be unsafe (the agent committed on its own,
so HEAD moved, or the claim commit already reached a remote) the run falls back to the old split:
a work commit plus a status commit. Good for solo projects or low-risk commands where a review
branch is overhead. `base:` requires `branch:`; a command that names a base without a
branch is rejected as malformed.
