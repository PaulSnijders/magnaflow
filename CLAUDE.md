# MagnaFlow

Monorepo of small deterministic tools for a spec-first AI development platform.
Read `.specify/memory/constitution.md` first — it governs all design decisions.

## Layout

- `docs/mf-spec/` — **current state** of the spec system (start here); the
  installable kit lives in `tools/mf-spec/spec-kit/`
- `docs/fase*/` and `docs/kennis/` — history: decisions, evaluations,
  design discussions (append-only context, not the current truth)
- `tools/<name>/` — one standalone C#/.NET console app per tool (own solution, src/, tests/)
- `specs/` — feature specs from the (retired) GitHub Spec Kit experiment; history

## Active work

- `mf-spec`: our own spec system; kit v0 built and adopted in real
  projects. GitHub Spec Kit is retired from the default workflow
  (see `docs/kennis/spec-strategie.md`).
- `mf-worker` CLI (v0.1 done; .NET 10, Spectre.Console.Cli, CliWrap,
  YamlDotNet) in `tools/worker-controller/`; v0.2 direction in
  `docs/fase2-worker-controller/v0.2-direction.md`.

## Rules of thumb

- Plain text (Markdown/YAML) + git for all state; never move files, change status in place
- Core logic gets xUnit tests; external processes go behind `IProcessRunner`/`IAgentRunner`
- KISS — no new dependencies without a concrete need
- New prompt in `docs/prompts/`: read the next `NNNN` off disk (highest number
  present + 1), never from what the conversation remembers — a resumed thread's
  view of the lane is stale. Follow-ups keep the parent's number: `0008B-`
