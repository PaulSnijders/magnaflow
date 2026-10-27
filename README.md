# MagnaFlow

A spec-first, git-native way to build software with an AI coding agent.
State specs (`docs/specs/`) are the AI's memory: a projection of the
code, ~20× smaller, that doubles as in-app help and team docs. Change
requests go through a git-committed lane (`docs/prompts/`). A worker
executes them (plan → implement → build → test, bounded retries) — or
you do, in a Claude Code session — and a human gates every merge. Everything is plain text; git is the
database. Full picture: `tools/mf-spec/README.md`; governing rules:
`docs/specs/concepts/design.md`.

## Install or update the spec kit — one prompt

Paste this into Claude Code in the target repo; it adopts the kit or
updates an installed one (details: [`tools/mf-spec/`](tools/mf-spec/README.md)):

```text
Install or update the MagnaFlow spec kit in this repo.
1. Download
   https://github.com/PaulSnijders/magnaflow/raw/main/tools/mf-spec/spec-kit.zip
   and unpack it into docs/ here, so docs/spec-kit/ holds the kit
   (replace an existing docs/spec-kit/). Delete the zip.
2. If docs/specs/README.md has a "spec-kit version" or "mf-spec kit
   version" comment, the kit is already adopted: follow
   docs/spec-kit/0002-update-spec-system.md. If there is no docs/specs/,
   follow docs/spec-kit/0001-adopt-spec-system.md. Anything else: ask me.
   Follow that file as your prompt, including its questions and its
   cleanup of docs/spec-kit/.
```

## The tools (`tools/<name>/`)

- **mf-worker** (`worker-controller/`) — one-shot controller: has an
  AI agent plan and implement a `ready` command on a work branch, runs
  build/test with bounded retries, commits status, logs and report.
- **mf-spec** — the spec system itself. Its installable kit,
  `mf-spec/spec-kit/`, is the normative source for target repos.
- **mf-watch** — polls a project's `docs/prompts/` for `ready` commands
  and hands them to `mf-worker`, forever, with adaptive backoff.
- **mf-cockpit** — read-only dashboard and chat over one or more
  projects' plain-text state, plus project onboarding ("Add project").
- **mf-run** — starts and stops a project's own dev/debug processes, so
  a worker run doesn't hit locked ports or files and the fresh build
  runs afterwards.

## Configuration

- **Per project**: `.magnaflow/config.yml` — build/test commands, the
  AI agent invocation, `run:` services. Read by mf-worker and mf-run.
  Specs have their own `docs/specs/config.yml` (surface declarations).
- **Per machine**: `magnaflow.yml` — one file with `watch:` and
  `cockpit:` sections; each tool ignores the other's section (see
  `docs/specs/concepts/machine-config.md`).

## Docs layout

MagnaFlow is built with its own spec system (`docs/specs/README.md`):

- `docs/specs/` — state specs: one surface per tool, plus `concepts/`
  (cross-tool mechanisms; `design.md` holds the governing principles).
- `docs/decisions/` — why it became this way (numbered; a decision that
  no longer holds gets a successor, not a rewrite).
- `docs/context/` — what the outside world said (ISO-dated, one
  conversation per file).
- `docs/prompts/` — this repo's own delta lane (cmd/rst pairs).

## Where to start

Two ways in; the spec kit works on its own, the tools build on it.

- **Specs only** — no tools, just Claude Code. Paste the prompt above
  in the target repo; answer "no" to the MagnaFlow question. You get the specs, the lane, `/architect`, `/brainstorm`,
  `/spec`, `/spec-drift` and `cc-review`. You execute a `ready` cmd
  yourself: `/cmd-go` in a fresh Claude Code session picks up the next
  one. Details: `tools/mf-spec/README.md`.
- **Full MagnaFlow** — the worker picks up `ready` cmds unattended.
  1. Install the tools per
     [`tools/install/README.md`](tools/install/README.md) (one script,
     Windows or Linux).
  2. Run mf-cockpit and add a project with `+`. It registers an
     existing working copy or scaffolds a new one, and seeds the adopt
     prompt as a draft (see `docs/decisions/0009-cockpit-add-project.md`).

You can start with specs only and add the tools later: paste the prompt
again (it runs the update) and answer "yes" to the MagnaFlow question.
