# mf-worker — Examples

A hands-on tour of every command, using the ready-made example project in
[`examples/hello-website/`](examples/hello-website/): three commands that let the AI agent
build a hello-world website. Reference docs: [README.md](README.md) and the
file-format contracts (`git show c0e1353:specs/002-plan-questions-feedback/contracts/file-formats.md`).

Commands below are PowerShell on Windows; the tool itself is cross-platform.

## 0. Prerequisites

```powershell
dotnet --version    # .NET 10 SDK
git --version
claude --version    # installed and authenticated (or configure another agent, see §7)

# Build the controller once:
cd C:\GIT\magnaflow\tools\worker-controller
dotnet build
# Optional: alias for convenience in this shell session
Set-Alias mf-worker "C:\GIT\magnaflow\tools\worker-controller\src\MagnaFlow.WorkerController\bin\Debug\net10.0\MagnaFlow.WorkerController.exe"
```

## 1. What a target project must contain

`mf-worker` operates on any git repository with `docs/prompts/` and `.magnaflow/` folders:

```text
my-project/                          # a git repo, on a branch, clean working tree
├── docs/
│   ├── prompts/
│   │   └── 0001-cmd-short-name.md   # REQUIRED per command: NNNN-name = ID = execution order
│   └── specs/…                      # optional: spec files that commands link via 'specs:'
├── .magnaflow/
│   └── config.yml                   # REQUIRED: build/test commands (+ optional defaults, agent)
└── src/…                            # whatever the agent will be working on
```

Minimum viable `config.yml`:

```yaml
build:
  command: echo nothing to build
test:
  command: if exist src\index.html (exit 0) else (exit 1)
```

Minimum viable command file:

```markdown
---
title: Create the hello world page
status: ready
branch: task/0001-hello-page
attempts: 0
---

## Goal
A homepage at `src/index.html` that greets visitors with "Hello, world!".
```

Everything else in the frontmatter (`branch`, `base`, `group`, `fresh_session`, `resume`,
`specs`, `max_attempts`, `created`) is optional — see §6. Without `branch:` the command runs
*branchless*: work is committed directly on the branch you run from.

## 2. Set up the example project

The example ships inside this repo but must run in its **own** git repository:

```powershell
# Copy the example somewhere outside the magnaflow repo:
Copy-Item -Recurse C:\GIT\magnaflow\tools\worker-controller\examples\hello-website C:\tmp\hello-website
cd C:\tmp\hello-website

# Make it a git repo with a clean initial state:
git init -b main
git add -A
git commit -m "hello-website: project skeleton + commands"
```

You now have three ready commands. Check:

```powershell
mf-worker status
```

```text
┌───────────────────┬──────────────────────────────┬─────────┬──────────┐
│ Command           │ Title                        │ Status  │ Attempts │
├───────────────────┼──────────────────────────────┼─────────┼──────────┤
│ 0001-hello-page   │ Create the hello world page  │ ready   │ 0/3      │
│ 0002-add-styling  │ Add shared styling            │ ready   │ 0/3      │
│ 0003-about-page   │ Add an about page             │ ready   │ 0/3      │
└───────────────────┴──────────────────────────────┴─────────┴──────────┘
```

## 3. `run` — execute one specific command

```powershell
mf-worker run 0001-hello-page
```

What happens, step by step (all visible in git afterwards):

1. Preconditions: valid config → git + agent available → clean tree → command `ready` →
   linked specs exist → base branch exists (when a new work branch is needed). Refusals
   change **nothing**.
2. `status: running` is committed on your current branch (`main`).
3. **Plan phase**: the agent reads the command, its linked specs, and the project's own
   conventions (a `CLAUDE.md` and/or constitution file, if present) and plans, in the session it
   will implement from. For a well-specified command like this one nothing is left open, so no
   `0001-pln-hello-page.md` is written and the run continues immediately; a `pln` appears only
   when the agent has a question for you, and the run then pauses on it.
4. Branch `task/0001-hello-page` is created from the default branch.
5. The agent implements the goal. Build and test commands run; failures are fed back to the
   agent (up to `max_attempts`).
6. The agent's work is committed on the command branch; back on `main`, the plan, the report
   (`0001-rst-hello-page.md`), the logs, `session.yml`, final `attempts`, and `status: done`
   are committed.

Exit code `0` = done (or a legitimate pause), `1` = aborted after max attempts (see §9 for all
codes).

### Running from another directory

Every command accepts `--project`:

```powershell
mf-worker run 0001-hello-page --project C:\tmp\hello-website
```

## 4. Inspect what a run did

```powershell
mf-worker status                                        # queue overview
git log --oneline main                                  # the orchestration audit trail
#   a1b2c3d mf-worker: 0001-hello-page -> done (1 attempt(s))
#   d4e5f6a mf-worker: 0001-hello-page -> running
#   ...

git show --stat task/0001-hello-page                     # the agent's work: pure code, no bookkeeping
ls docs\prompts\0001-pln-hello-page.md                   # absent — this command raised no questions
cat docs\prompts\0001-rst-hello-page.md                  # the report: what was done, and why
cat .magnaflow\0001-hello-page\session.yml               # the agent session ID this run ended with
cat .magnaflow\0001-hello-page\claude.log                # raw agent output (plan phase + every attempt)
cat .magnaflow\0001-hello-page\build.log                 # build output per attempt
cat .magnaflow\0001-hello-page\test.log                  # test output per attempt
```

Review the work like any branch, and merge it yourself when satisfied — mf-worker never merges:

```powershell
git diff main...task/0001-hello-page
git merge task/0001-hello-page
```

Prefer reviewing *without* checking the work branch out (`git diff`/`git show`): a work branch
carries a frozen mid-run snapshot of the bookkeeping, so mf-worker refuses to run from one (see
§8). If you do check it out to try the result in a browser, switch back to `main` before the next
mf-worker command.

## 5. `next` and `run-all` — queue-driven execution

```powershell
mf-worker next       # picks the first ready command (lowest ID) and runs it
mf-worker next       # ...the next one, and so on
mf-worker next       # "nothing ready" + exit 0 when the queue is empty

mf-worker run-all    # drains ALL ready commands sequentially, prints a summary, stops
```

```text
┌───────────────────┬─────────┐
│ Command           │ Outcome │
├───────────────────┼─────────┤
│ 0002-add-styling  │ done    │
│ 0003-about-page   │ done    │
└───────────────────┴─────────┘
```

`run-all` never waits for new work (one-shot by design), continues past an `aborted` command,
and stops early only on systemic problems (dirty tree, missing environment). A command paused at
`questions` is **not** picked up automatically — see §6a.

## 6. Command frontmatter recipes

The example's commands 0002 and 0003 demonstrate the two most useful options:

**Stacking on unmerged work — `base:`.** Command 0002 needs the page 0001 created, but you
haven't merged `task/0001-hello-page` yet. Point its branch base at the predecessor:

```yaml
branch: task/0002-add-styling
base: task/0001-hello-page
```

Without `base:`, branches start from the repository's default branch.

**Shared agent session — `group:`.** Consecutive commands with the same group reuse one Claude
session during a single `run-all`, so command 0003's agent still remembers the context of 0002:

```yaml
group: website
```

**Continue context across invocations — `resume:`.** Every run records the session ID it ended
with in `.magnaflow/<id>/session.yml`. Point a later command at it — even days later, in a fresh
invocation:

```yaml
resume: 0002-add-styling             # a command ID: continue that command's recorded session
```

Or continue a specific Claude session directly (any value that is not a command ID is passed to
the agent verbatim — e.g. a session from an interactive `claude` conversation):

```yaml
resume: 6a1f0e6e-9c1d-4f5a-b0e2-3d8f19a7c44e
```

`resume:` beats group continuity and the default self-resume (§6a); combining it with
`fresh_session: true` is rejected. Referencing a command that never completed a run is a clear
error before anything is touched.

**Force a fresh session** despite a matching group (e.g. a big context switch):

```yaml
fresh_session: true
```

**Give the agent background reading** — file contents are inlined into every phase's prompt; a
missing path aborts the run before the agent starts:

```yaml
specs:
  - docs/specs/website.md
  - docs/decisions/0004-css-conventions.md
```

**Tune retries per command** (overrides `defaults.max_attempts` from config.yml; shared by
planning crashes and build/test retries alike — a clean pause never counts against it):

```yaml
max_attempts: 1    # e.g. a mechanical rename that should work first try
```

**No branching at all** — omit `branch:` and the work is committed directly on the branch you run
from (its own commit, separate from the status commits). Handy for solo projects or low-risk
chores; you lose the reviewable branch, git history is still complete:

```markdown
---
title: Fix the typo in the footer
status: ready
attempts: 0
max_attempts: 1
---

## Goal
The footer says "Copyrigth"; make it "Copyright".
```

### 6a. When the plan raises a genuine question

Suppose a command's goal is genuinely ambiguous — nothing in its specs, the code, or the
project's conventions settles it. The run pauses instead of guessing:

```powershell
mf-worker run 0004-add-banner
# [0004-add-banner] paused: 1 question(s) need a human answer — see 0004-qa-add-banner.md
```

```powershell
mf-worker status
```

```text
│ 0004-add-banner   │ Add a promo banner            │ questions │ 0/3 │
```

Open `docs\prompts\0004-qa-add-banner.md`:

```markdown
## Question (round 1)

Should the banner link to the pricing page or the signup page?

**Answer**:
```

Write your answer beneath it, then set the command's own `status:` back to `ready`:

```markdown
**Answer**: The signup page — that's this quarter's growth goal.
```

Run it again — the controller resumes the *exact* agent session that paused (no re-explaining
context), re-plans by updating the same plan file with your answer, and continues:

```powershell
mf-worker run 0004-add-banner
# [0004-add-banner] resuming this command's own recorded session (self-resume)
```

If the plan raises a further question, the same cycle repeats — a second round is appended to
the same `qa` file rather than starting a new one. Running a command while it is still `questions`
(before you've reset it to `ready`) is refused, the same as any other non-`ready` status.

## 7. config.yml recipes

**Full autonomy on your own machine** (skips Claude Code's per-action permission prompts —
deliberate opt-in, keep it out of shared projects unless the team agrees):

```yaml
agent:
  command: claude
  args: [--dangerously-skip-permissions]
```

**A different agent entirely** — anything that reads a prompt on stdin and behaves headless can
be dropped in; mf-worker contains no Claude-specific logic beyond flags:

```yaml
agent:
  command: my-agent-cli
```

**Real build/test commands** (a .NET project instead of a static site):

```yaml
build:
  command: dotnet build src
test:
  command: dotnet test tests
defaults:
  command_timeout_minutes: 45   # slow test suite? raise the per-command timeout
```

**Monorepo with more than one stack** — `commands:` runs each entry in order and stops at the
first failure, whose output becomes the feedback:

```yaml
build:
  commands:
    - dotnet build web/Wozzol.sln
    - npm --prefix wozzol-ionic run build
test:
  commands:
    - dotnet test web/Wozzol.Tests/Wozzol.Tests.csproj
```

**Project conventions** — drop a `CLAUDE.md` at the repository root and/or a constitution file
at `.specify/memory/constitution.md` (or `docs/constitution.md`); mf-worker includes whichever
exists in every phase's agent instructions automatically. No config needed, and no error if
neither exists.

## 8. When things go wrong

**Dirty working tree** — runs are refused before anything changes:

```powershell
echo x > notes.txt
mf-worker next
# working tree has uncommitted changes; commit or stash them first (command stays ready)
# exit code 3
```

**Standing on a work branch** — every command refuses (or warns, for `status`), because a work
branch's bookkeeping is a frozen mid-run snapshot, not the live queue:

```powershell
git checkout task/0001-hello-page   # e.g. to view the page
mf-worker next
# mf-worker: you are on 'task/0001-hello-page', the work branch of command 0001-hello-page
# mf-worker: command state on a work branch is a frozen mid-run snapshot - the queue here is not the live queue,
# mf-worker: and running from here would pollute the work branch with orchestration commits
# mf-worker: switch back to your orchestration branch first:  git checkout main
# exit code 3
```

**Command stuck in `running`** (controller was killed mid-run):

```powershell
mf-worker run 0002-add-styling
# [0002-add-styling] is marked running — a previous run may have been interrupted;
# inspect the command and reset status to ready manually if appropriate
```

Recovery is a plain-text edit: look at the logs and git history, set `status: running` back to
`status: ready` in the command file, commit, and run again. The work branch is reused, so nothing
is lost.

**Aborted after all attempts** — the command ends `aborted` (exit 1), every attempt's logs are in
`.magnaflow/<id>/`, and the report (`NNNN-rst-*.md`) names the specific reason. Fix the command
description (or the code) and reset the status to `ready` to try again.

## 9. Exit codes — scripting and composition

```text
0  requested work succeeded, or a legitimate pause occurred (command done / paused at
   questions / batch done / status shown / nothing ready)
1  command(s) ended aborted after max attempts
2  usage or configuration error (unknown command, invalid config, missing spec/base branch)
3  precondition refusal, nothing mutated (dirty tree, command not ready)
4  environment error (git or agent executable unavailable)
```

Example: a poor man's dispatcher that drains the queue and alerts on trouble:

```powershell
mf-worker run-all --project C:\tmp\hello-website
switch ($LASTEXITCODE) {
    0 { Write-Host "queue drained, all done (check for any paused at questions)" }
    1 { Write-Host "queue drained, but some commands aborted - review the branches" }
    default { Write-Host "controller could not run (exit $LASTEXITCODE)" }
}
```
