# Technical

What one worker run of one command does, from refusal checks to the
final commit. All four subcommands that execute (`run`, `next`,
`run-all`) go through this one loop (`TaskRunner.RunAsync`). Statuses and
file formats are in [command lifecycle](command-lifecycle.md); the
runtime files in [evidence layout](evidence-layout.md).

## Preconditions

Checked in this order. Nothing is written or committed until all pass.

| Check | Exit |
|---|---|
| command malformed (bad frontmatter, orphaned sibling) | 2 |
| `git --version` fails | 4 |
| `<agent.command> --version` fails or is not found (60 s limit) | 4 |
| working tree dirty (`git status --porcelain` non-empty) | 3 |
| current branch is some command's `branch:` (work-branch guard) | 3 |
| status is not `ready` (separate hint for `running` and `questions`) | 3 |
| a `specs:` file does not exist | 2 |
| `resume:` does not resolve ([session resume](session-resume.md)) | 2 |
| `branch:` is new and no base resolves, or `base:` does not exist | 2 |

- **Dirty-tree guard**: the worker refuses rather than mix someone
  else's changes into its commits. A human's qa answer and `ready` flip
  must therefore be committed before the next run. This is also why
  `.magnaflow/*` is gitignored ([evidence layout](evidence-layout.md#why-machine-local)).
- **Work-branch guard**: on a work branch the lane is a frozen mid-run
  snapshot. Orchestrating from there would misread the queue and write
  bookkeeping onto the work branch. The message names the default branch
  to check out. The subcommands check this before scanning too.
- A detached HEAD fails the branch lookup and ends the run with exit 4.

## The run

1. **Claim**: write `status: running`, commit only the cmd file
   (`mf-worker: <id> -> running`). No push.
2. **Plan phase**, still on the invoking branch, once per run. The agent
   gets the plan prompt and replies with its plan, which stays in the
   session. It writes a pln only when something needs a human. Open
   questions are copied into the qa file, the status becomes `questions`,
   the run commits and exits 0. Build and tests are skipped, no work
   branch is created, and mf-run is neither stopped nor started.
3. **mf-run stop**, only when the project has `run.services`. It runs
   right before implementation, so the app keeps running through
   planning and pauses. A failed stop (non-zero, or not found) refuses
   the implementation. The status reverts to `ready`, that is committed,
   the run exits 3, and no execution attempt is consumed.
4. **Branch**: no `branch:` means work on the invoking branch. An
   existing branch is checked out; a new one is created from `base:`, or
   else from the default branch (`origin/HEAD`, then `main`, then
   `master`).
5. **Implement, build, test**, in the same agent session. Each attempt
   runs the agent, then every `build` command in order, then every `test`
   command. The first failing command stops its section. Its output
   (the last 16 000 chars) goes back to the agent as the next prompt.
6. **Work commit**: `git add -A` of everything except `.magnaflow/` and
   `docs/prompts/`. In branch mode it is committed as `command <id>:
   <title>`, the work branch is pushed, and the invoking branch is
   checked out again. Branchless, the work stays staged.
7. **Terminal**: write `attempts:` and `done` or `aborted`, write the
   fallback rst if the agent wrote none, run `mf-run start` (when
   configured), then make the terminal commit and push the invoking
   branch.

**Aborted work is committed too.** Whatever the agent left in the tree
after the last failed attempt lands in the work commit (or the folded
commit on trunk) and is pushed. Nothing is discarded, so a human can
inspect it. A branchless abort therefore puts failing code on the
invoking branch.

## Attempts and timeouts

- `max_attempts` (per command, else `defaults.max_attempts`, default 3,
  at least 1) bounds the whole run. `attempts:` in the cmd file counts
  across runs and pauses.
- A plan-phase agent crash or timeout costs one attempt. A successful
  plan does not. Each implementation attempt costs one, whether the
  agent, the build or the tests failed.
- `defaults.command_timeout_minutes` (default 30) applies to each agent
  invocation and to each individual build or test command. A timeout
  counts as a failure of that step. git calls have a fixed 2-minute
  limit. `mf-run stop` and `mf-run start` have none.
- BUG: when a plan-phase crash yields no session id, the retry sends
  only the failure-feedback text into a fresh session, without the
  command. That feedback also reads "the agent step failed after your
  changes", even though there were no changes.

## mf-run around the run

Only when `.magnaflow/config.yml` has at least one `run.services`. The
worker knows nothing else about services. It spawns `<run.command>
stop|start --project <root>` (default `mf-run`) and reads only the exit
code ([mf-run](../run/mf-run.md#integration)). Start runs after both
`done` and `aborted`, because the last build is often how an abort is
diagnosed. A failed start is a `## Warning` in the rst, never a retry and
never a status change.

## Branches

Two planes ([design](design.md#iii-git-is-the-database)): bookkeeping
(cmd, pln, qa, rst) commits on the **invoking** branch, and code plus
its spec updates on the work branch. Note that this is the branch
mf-worker was started from, not `base:`. Merging is human. The worker
pushes but never opens a pull request.

**Branchless, one commit per run**: the claim commit is amended at the
end to carry the work, the bookkeeping and the terminal status, as
`command <id>: <title>`. The amend happens only when all of these hold,
and they are checked right before it:

- the run reached the work plane;
- HEAD is still the claim commit, so the agent did not commit on its own;
- the claim commit is on no remote-tracking ref. An unanswerable check
  counts as "on a remote".

Otherwise the run lands as a work commit plus `mf-worker: <id> ->
done|aborted (N attempt(s))`. A pause, a stop refusal or a planning-only
abort is always two commits (claim plus outcome).

**Push**: when a remote exists (`origin`, else the first one), the work
branch is pushed after its commit and the invoking branch after each
outcome commit, with `--set-upstream`. A failed push is a warning. The
commits stay local and the exit code is unchanged.

**Lost terminal commit**: if staging cmd, pln or rst, or the commit
itself, fails, the run says on stderr which command it was, that its
status and rst are written but uncommitted, and that the next run will
refuse on a dirty tree. Exit 4. Staging the evidence directory can never
cause this; see [evidence layout](evidence-layout.md#what-the-worker-commits).
A git failure anywhere else mid-run also exits 4 and leaves the command
committed as `running` for a human to reset.

## Agent invocation

`<agent.command> -p --output-format stream-json --verbose [--resume
<session>] <agent.args...>`, run in the project root. The prompt goes in
on stdin, because specs can be large. Every raw line is streamed to
`claude.log`. The session id and final text are picked from the JSON
lines. Anything else about autonomy (permission flags) comes verbatim
from `agent.args`. The worker adds none, because headless Claude cannot
ask and a denied tool ends the run. See [machine and project
config](machine-config.md) for the config file.

Every prompt starts with the same rules. The agent stays in scope and
never touches `.magnaflow/`, the cmd file or the qa file. It never runs
`git commit/branch/checkout/push`, because the worker owns git. The
implementation prompt pins the rst's four frontmatter keys and asks for
a "Self-answered questions" section.

## Conventions loading

Each phase's prompt carries the **target** project's conventions:
`CLAUDE.md` at its root, plus the first existing of
`.specify/memory/constitution.md` and `docs/constitution.md`. Both
sources are included when both exist, joined by a `---` rule. Missing
files are skipped silently. The paths are fixed, not configurable. They
are followed by the full text of every `specs:` file and then the cmd
body.

DRAFT: generated from code, not human-reviewed.

Code: tools/worker-controller/src/MagnaFlow.WorkerController/Execution/TaskRunner.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Execution/PromptBuilder.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Execution/ConventionLoader.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Agents/ClaudeCodeRunner.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Config/ProjectConfig.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Infrastructure/GitClient.cs, tools/worker-controller/src/MagnaFlow.WorkerController/Commands/CommandInfrastructure.cs
Why: decisions/0005-worker-v0-2-direction.md, prompts/0011-rst-worker-one-commit-per-run.md, prompts/0015-rst-plan-file-only-on-questions.md
