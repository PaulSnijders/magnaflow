# Technical

MagnaFlow is a spec-first, git-native way to build software with an AI
coding agent. A human, or a design session working only in `docs/`,
writes state specs and commands. A worker executes the commands. Every
step leaves plain-text evidence in the target repo. There is no
server-side state: the target repository is the database. See the
principles in [design](concepts/design.md).

## The tools

| Tool | Surface | Role |
|---|---|---|
| mf-worker | [worker](worker/run.md) | One-shot: execute one `ready` command (plan, implement, build, test, report) |
| mf-watch | [watch](watch/mf-watch.md) | Long-running per project: poll the lane, dispatch to mf-worker, back off when idle |
| mf-run | [run](run/mf-run.md) | One-shot: start/stop the project's own dev processes around a worker run |
| mf-cockpit | [cockpit](cockpit/index.md) | Web window on one or more projects: lane, evidence, git, watcher, chat; writes only drafts and `draft → ready` |
| mf-spec | — | The spec system itself, installed into target repos as files; described in `tools/mf-spec/` |

The tools compose by spawning each other's executables, never by library
reference. Each one can be replaced or run by hand.

## The flow

1. A command is written to `docs/prompts/NNNN-cmd-*.md` as `draft`, by
   hand, from a design session, or via the cockpit. A human marks it
   `ready`. See [command lifecycle](concepts/command-lifecycle.md).
2. mf-watch sees `ready` and spawns mf-worker. See
   [watch supervision](concepts/watch-supervision.md).
3. mf-worker runs the loop in [worker run](concepts/worker-run.md). It
   ends in `done`, `aborted`, or `questions` (a human answers in the qa
   file and sets `ready` again).
4. The rst report and its `summary:` line are the committed account. Raw
   logs stay machine-local, see [evidence layout](concepts/evidence-layout.md).
5. The specs in the target repo were updated in the same commit
   (spec-first). `/spec-drift` audits that.

## Configuration and installation

There are two axes: per project (`.magnaflow/config.yml`, in git) and per
machine (`magnaflow.yml`). See [machine config](concepts/machine-config.md).
Tools are published and wired up by `tools/install/`, see
[machine install](concepts/machine-install.md).

## This repo

MagnaFlow is built with itself. This repo is a MagnaFlow project with its
own lane and specs. The kit master in `tools/mf-spec/spec-kit/` is also
installed here. Rolling out new tool code stays a manual
`install.ps1` / `install.sh`.
