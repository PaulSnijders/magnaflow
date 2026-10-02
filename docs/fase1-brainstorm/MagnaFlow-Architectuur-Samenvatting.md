# MagnaFlow -- Architecture discussion summary

## Main idea

MagnaFlow is not a new AI or code editor, but a spec-first
development platform that smartly combines existing AI tools. The repository
and the knowledge are the source of truth; AI is an interchangeable
executor.

## Development process

1.  Start a new project from a template and chosen stack.
2.  Work out ideas in a chat based on project knowledge.
3.  AI writes and maintains specs.
4.  Human accepts the specs.
5.  Specs are split into executable tasks.
6.  Coding workers implement the tasks.
7.  Build and tests run automatically.
8.  Results are reviewed.
9.  From the running application back to the specs to iterate
    further.

## Main components

-   Project templates
-   Main Workspace (documentation, specs, tasks, decisions)
-   File editor (not a full code editor)
-   Spec Worker
-   Coding Worker
-   Test Runner
-   Build & Deploy
-   Runtime Bridge
-   Manual Development Line

## Worker Controller

The first MagnaFlow tool automates: - fetching a task - building the
prompt - starting Claude Code - waiting for the result - running
build/tests - giving follow-up prompts - committing - writing results back

## Flow

There is a distinction between: - **Flow Definition** (recipe) - **Flow
Instance** (one execution) - **Run Status** (current status) - **Step
Files** (status, logs, output and artifacts per step)

Every flow knows who its owner is (human, spec worker, coding worker,
test runner or reviewer), so human and AI can work in parallel.

## Repository

> **Updated:** the structure is pinned down in
> `docs/fase2-worker-controller/workflow-v0.1.md` — that document is the
> single source of truth.

``` text
docs/                 # knowledge and specs
src/                  # implementation
tests/

.magnaflow/
  config.yml          # build/test commands, retry limit
  tasks/
    0001-name/        # task folder: task.md (definition + status) + logs + result.yml
```

Brainstorm ideas such as `flows/`, `instances/`, `workers/` and `prompts/`
are not pinned down yet; they will return once the dispatcher is
designed.

## Runtime Bridge

Connects the running application to the workspace via logs,
error messages, browser context, screenshots and component information.

## Distributed Workers

The laptop is the cockpit. Heavy tasks (code generation, builds, tests and
analyses) run on one or more worker machines.

## Dashboard

MagnaFlow must be a 'glass box'. A dashboard shows: - current phase -
active workers - progress - build status - test status - review status -
who is currently up

## Conclusion

MagnaFlow's differentiation is not a better AI, but a
transparent, reproducible and spec-first development process in which
existing AI tools work together.
