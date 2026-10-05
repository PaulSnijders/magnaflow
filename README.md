# MagnaFlow

A spec-first, git-native way to build software with an AI coding agent.
State specs (`docs/specs/`) are the AI's memory — a compact projection
of the code, ~20× smaller, that also doubles as in-app help and team
documentation. Change requests flow through a git-committed lane
(`docs/prompts/`), a worker executes them (plan → implement → build →
test, bounded retries), and a human gates every merge. Everything is
plain text; git is the database. Full picture: `tools/mf-spec/README.md`
+ `tools/mf-spec/system.md`; governing rules: `docs/specs/concepts/design.md`.

## The tools (`tools/<name>/`)

- **mf-worker** (`worker-controller/`) — one-shot controller: picks up
  a `ready` command, has an AI agent plan and implement it on a work
  branch, runs build/test with bounded retries, commits status/logs/
  report as plain text.
- **mf-spec** — the spec system itself (`mf-spec/README.md` and
  `mf-spec/system.md` describe it; the installable kit is
  `mf-spec/spec-kit/`, the normative source for target repos).
- **mf-watch** — polls a project's `docs/prompts/` for `ready` commands
  and dispatches them to `mf-worker`, forever, with adaptive backoff.
- **mf-cockpit** — read-only dashboard + chat over one or more
  projects' plain-text state, plus project onboarding ("Add project").
- **mf-run** — starts/stops a project's own dev/debug process(es) so a
  worker run doesn't fight locked ports/files, and the fresh build is
  already up afterwards.

## Configuration

- **Per project**, `.magnaflow/config.yml` — build/test commands, the
  AI agent invocation, `run:` services; read by both mf-worker and
  mf-run. Specs have their own `docs/specs/config.yml` (surface
  declarations).
- **Per machine**, `magnaflow.yml` — one file, `watch:` / `cockpit:`
  sections, each tool ignoring the other's section (see
  `docs/specs/concepts/machine-config.md`).

## Docs layout

MagnaFlow is built with its own spec system (`docs/specs/README.md`):

- `docs/specs/` — state specs of the tools: one surface per tool, plus
  `concepts/` (cross-tool mechanisms; `design.md` holds the governing
  principles).
- `docs/decisions/` — why it became this way (numbered, frozen).
- `docs/context/` — early brainstorm material and outside observations.
- `docs/prompts/` — this repo's own delta lane (cmd/rst pairs).

## Where to start

Run mf-cockpit, add a project via `+` (registers an existing working
copy or scaffolds a new one and seeds the adopt prompt as a draft — see
`docs/decisions/0009-cockpit-add-project.md`), or adopt the spec kit
manually per `tools/mf-spec/README.md`.
