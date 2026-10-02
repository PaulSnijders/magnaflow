# Step-by-step plan — specifying & building the Worker Controller with GitHub Spec Kit

> Spec Kit = GitHub's spec-first toolkit. It installs slash commands in
> Claude Code that guide you through Spec → Plan → Tasks → Implement, per feature
> on its own git branch. We use it to build the Worker Controller
> and to experience whether this approach works well for MagnaFlow.

## Repo structure (decided 2026-07-02)

MagnaFlow is a monorepo of small tools; each tool gets its own
subfolder, Spec Kit lives at the repo root:

```text
C:\GIT\magnaflow\
  docs/                      # vision, knowledge, workflow
  tools/
    worker-controller/       # own .NET solution, standalone program
      src/
      tests/
    dispatcher/              # later
  .specify/                  # Spec Kit, repo-wide
```

Why Spec Kit at the root and not in tools/worker-controller/: Spec Kit is
git-branch-based (feature = branch), and branches belong to the repo —
not to a subfolder. Moreover, this way a single constitution applies to all tools.
Where the code lands is passed in `/speckit.plan`. Why a monorepo: docs
and specs are the source of truth and stay together this way; splitting per
tool remains possible later.

Task numbering in `.magnaflow/tasks/`: four digits (`0001-name`).

## Step 1 — Installing Spec Kit (detailed)

Prerequisites: git, Claude Code, and `uv` (or `pipx`). Check in PowerShell:

```powershell
git --version
claude --version
uv --version        # if not: winget install astral-sh.uv
```

Then initialize Spec Kit **in the magnaflow repo** (C:\GIT\magnaflow):

```powershell
cd C:\GIT\magnaflow
uvx --from git+https://github.com/github/spec-kit.git specify init . --integration claude
```

What this does: it puts down a `.specify/` folder (templates for spec.md,
plan.md, tasks.md + constitution) and installs the `/speckit.*` slash
commands for Claude Code. On Windows it automatically picks
PowerShell scripts. No existing files are changed.

Verify: open Claude Code in the repo and type `/speckit.` — the commands
should appear in the autocomplete.

## Step 2 — Establish the constitution

In Claude Code: `/speckit.constitution` with the MagnaFlow principles as input
(from fase1-brainstorm): small deterministic tools, everything plain text
(Markdown + YAML), git is the database, observable over magical, AI is
interchangeable, restartable and idempotent. Plus: C#/.NET, no AI logic in the
controller itself.

This is a one-time step per project and steers all later phases.

## Step 3 — Write the spec

`/speckit.specify` with as input: the **what and why** of the Worker
Controller. Pass `workflow-v0.1.md` and the fase1 summary along as context.
Spec Kit automatically creates a branch (e.g. `001-worker-controller`) and a
spec file. Do not mention technology yet — that comes in step 5.

## Step 4 — Clarify and validate

`/speckit.clarify` (resolves ambiguities interactively) and then
`/speckit.checklist` as a quality check on the requirements.

## Step 5 — Technical plan

`/speckit.plan` with the stack: C#/.NET console app, Spectre.Console.Cli,
CliWrap, YamlDotNet; Claude Code headless via `claude -p`. State
explicitly: code in `tools/worker-controller/` (src/ and tests/ inside it).

## Step 6 — Tasks, analysis, implementation

`/speckit.tasks` → `/speckit.analyze` (consistency check spec/plan/tasks)
→ `/speckit.implement`. Implement in phases: first get `run <task-id>`
working end-to-end, only then `next` and `status`.

## Step 7 — Evaluate

Afterwards briefly record: what worked well about Spec Kit, what did not, and what
MagnaFlow's own spec layer should do differently. That is the actual
research question of this phase.
