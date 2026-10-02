# Phase 0 Research: Worker Controller — Plan, Questions & Feedback (v0.2)

No `[NEEDS CLARIFICATION]` markers remain in `spec.md` (all resolved during `/speckit-clarify`,
2026-07-08). This phase resolves the remaining *design* unknowns needed before Phase 1 — how the
spec's requirements map onto the existing `tools/worker-controller/` codebase.

## R1: Vocabulary and file-model migration (Tasks/ → Prompts/)

**Decision**: Rename the domain vocabulary throughout the codebase from "task" to "command"
(`cmd`), matching the spec: `TaskFile` → `CmdFile`, `TaskState` → `CmdStatus`, `TaskScanner` →
`PromptScanner`, the `Tasks/` namespace folder → `Prompts/`. Add three new file types
(`PlnFile`, `QaFile`, `RstFile`) to the same namespace.

**Rationale**: The spec (`specs/002-plan-questions-feedback/spec.md`) and the mf-spec system
(`docs/mf-spec/`) already use "command"/"cmd" as the canonical term for `docs/prompts/`'s unit of
work; keeping the old "task" name in code while the spec says "command" would make the code and
its own spec disagree on vocabulary — a drift the constitution's spec-first rule exists to
prevent. This is a pre-1.0 internal tool with one consumer (this monorepo); no external API,
so no back-compat shim is needed (CLAUDE.md: "don't use ... backwards-compatibility shims when
you can just change the code").

**Alternatives considered**: Keep "Task"/"TaskFile" names and only change on-disk paths. Rejected:
cheaper short-term, but leaves code and spec permanently out of sync in terminology, which the
constitution's Principle II (plain text as interface, readable by humans and AI alike) argues
against — code should read like the spec it implements.

## R2: `docs/prompts/` scanning and identity

**Decision**: `PromptScanner` scans `docs/prompts/` (flat, not recursive) for files matching
`^\d{4}-cmd-[a-z0-9-]+\.md$`. The `NNNN` + trailing `name` slug (everything after `cmd-`) is the
command's identity. Its optional siblings are found by substituting the infix:
`{NNNN}-pln-{name}.md`, `{NNNN}-qa-{name}.md`, `{NNNN}-rst-{name}.md`. A pln/qa/rst file present
without a matching cmd file is reported as an orphaned/malformed entry (spec edge case) and
skipped — mirrors v0.1's "malformed task, report and skip, never guess" handling.

**Rationale**: Directly implements FR-001; a fixed infix convention (`cmd`/`pln`/`qa`/`rst`) is a
simple, deterministic parse (no directory listing/heuristic matching needed beyond one regex per
kind) — consistent with Principle I (deterministic tools).

**Alternatives considered**: Store command identity as an opaque frontmatter field instead of
deriving it from the filename. Rejected: the constitution already establishes (Principle II, and
v0.1's FR-001) that identity lives in the filename/foldername, never duplicated inside the file;
the prompt lane's own convention (`docs/mf-spec/README.md`) already fixes this file-naming scheme,
so there is nothing to decide here beyond parsing it.

## R3: The plan-phase/execution-phase gate, in one run

**Decision**: `TaskRunner` (kept as the execution-loop class name for now — it orchestrates a
command's run, not a single "task" concept) gains a plan phase that runs *before* its existing
attempt loop, in the same method, using the same `resumeSession` variable already threaded through
the loop:

1. Build the plan-phase prompt (command body + linked specs + project conventions).
2. Invoke `agent.RunAsync(...)`, same `IAgentRunner` used for execution — no new agent
   abstraction needed (constitution V: AI is a replaceable executor via one seam).
3. Parse the agent's structured reply into "plan text" + "self-answered Q&A" + "open questions"
   (see R5) and write/update the pln file.
4. Gate: open questions present → append to qa, set status `questions`, return — the existing
   attempt loop is never entered this run. No open questions → fall through directly into the
   existing build/test attempt loop, continuing the same `resumeSession`.

**Rationale**: Directly implements FR-008–FR-011. Reusing the same session-threading variable that
already carries `resumeSession` across retries (v0.1 FR-012) is the natural mechanism for "same
run, same session, no pause" (FR-010) — no new session-continuity concept is needed for the
in-run case, only for the *cross-run* resume case (R4).

**Alternatives considered**: A separate `PlanRunner` class fully decoupled from `TaskRunner`.
Rejected for v0.2: the plan and execution phases share one session and one precondition set
(clean tree, agent available, etc.) and must run in the same method to guarantee the "no pause
without a reason" gate is atomic with entering the attempt loop; splitting them would require
re-threading the same state across two classes for no benefit. (A future split remains possible
if the plan phase grows independent concerns.)

## R4: Cross-run resume (self-resume on re-plan)

**Decision**: Machine runtime evidence in `.magnaflow/<NNNN-name>/` gains one new small file,
`session.yml`, holding just `session: <id>` — written whenever a run ends (paused or terminal)
with a known session ID. `ResumeResolver`'s default (no explicit `resume:`/`group`) becomes: if
`.magnaflow/<NNNN-name>/session.yml` exists, resume that session; otherwise start fresh. This
replaces `result.yml` as the session-of-record (R6 also retires `result.yml`'s other fields).

**Rationale**: Implements FR-013. Keeping it a minimal, single-purpose file (just the session id)
matches FR-004's constraint that `.magnaflow/` holds *only* runtime evidence — a leftover
`result.yml` with a `summary:` field would be bookkeeping/content, which now belongs in the rst
file instead.

**Alternatives considered**: Store the session ID in the cmd frontmatter itself. Rejected: FR-004
explicitly keeps session IDs out of `docs/prompts/` (that's the bookkeeping plane); also, a
frontmatter field would be "content" the executor might be tempted to touch, and FR-005 forbids
the executor from ever writing the cmd file.

## R5: Agent reply contract for the plan phase

**Decision**: Reuse the existing free-text `IAgentRunner.RunAsync` contract (no new structured
JSON schema requirement on the agent). The plan-phase prompt instructs the agent to write its
plan, self-answered Q&A, and any open questions directly into `NNNN-pln-name.md` (and, if there
are open questions, to also state them in its final response), rather than requiring the
controller to parse a rigid JSON envelope out of the agent's reply. The controller's own signal
for "did this pause?" is simply: does the qa file gain new unanswered question(s) after this
invocation (a plain-text diff/parse of the qa file), not a machine-readable flag from the agent.

**Rationale**: v0.1 already treats the agent as a black box whose *files* are the source of truth
(constitution II/V) — `PromptBuilder`/`IAgentRunner` never parse the agent's stdout for anything
but the session ID and a display summary. Requiring a strict JSON contract from the agent for
plan/pause signaling would add AI-specific parsing logic to the controller (constitution V
violation risk) and duplicate what the file already says. The direction doc's mention of
`--output-format json --json-schema` (`docs/fase2-worker-controller/v0.2-direction.md`) was a
draft mechanism from before the mf-spec prompt lane existed; now that pausing is signaled by the
qa file's own content (an open question with no answer beneath it), no separate structured-output
contract is needed.

**Alternatives considered**: Force a `--json-schema` structured reply (`{outcome: done|questions,
questions: []}`) as originally sketched in the v0.2 direction doc. Rejected: redundant now that the
qa/pln files are themselves the machine-readable signal, and it would require the controller to
understand and validate agent-specific JSON — more AI-specific logic than necessary.

## R6: Retiring `result.yml`; `ResultWriter`'s replacement

**Decision**: `result.yml` is retired. Its three responsibilities split three ways:
- **status** → already in cmd frontmatter (unchanged from v0.1).
- **session id** → `.magnaflow/<NNNN-name>/session.yml` (R4).
- **human summary** → the rst file (`NNNN-rst-name.md`), now required to be a fuller narrative
  (FR-017/FR-018), not one paragraph.

`ResultWriter` is replaced by two small, focused pieces: a `SessionEvidence` writer/reader
(`.magnaflow/`) and an `RstFile` writer (`docs/prompts/`).

**Rationale**: Implements FR-004's split of bookkeeping vs. runtime evidence; avoids one class
straddling both planes, which was easy in v0.1 (one task folder held everything) but would
obscure the two-plane split now that they are different directories entirely.

## R7: Convention loader

**Decision**: A new static helper, `ConventionLoader.Load(projectRoot)`, returns the concatenated
content of whichever of these exist, in order: `CLAUDE.md` (repo root), then
`.specify/memory/constitution.md`, else `docs/constitution.md`. `PromptBuilder` calls it once per
phase (plan and execution) and prepends the result to the existing preamble, before the
command body and linked specs.

**Rationale**: Implements FR-020/FR-021 exactly as clarified — fixed paths, first-hit-wins for the
constitution check, no config schema change, silent skip when absent.

**Alternatives considered**: none seriously — the clarification session already settled this
(Option A, fixed paths) over a configurable-path alternative.

## R8: Exit codes and `aborted`

**Decision**: Keep the existing `ExitCodes` vocabulary (`Success`, `TaskFailed`, `UsageError`,
`PreconditionRefused`, `EnvironmentError`) unchanged in value, but reinterpret `TaskFailed` (exit
1) as "the command ended `aborted`" (any reason), matching FR-018. A run ending `questions` is
**not** a process failure — it exits `Success` (0), since pausing for a legitimate human decision
is normal, successful controller behavior, not an error condition (mirrors how v0.1 treats "no
task pending" as a clean `Success` exit, not a failure).

**Rationale**: Preserves the existing composability contract (FR-021 of v0.1, unchanged) — a
future dispatcher can already distinguish "ended aborted" (1) from "ran fine" (0) without knowing
which of `done`/`questions` occurred; distinguishing those two only matters to a human reading
`docs/prompts/`, not to exit-code-level composition.

## R9: Testing approach

**Decision**: Unchanged from v0.1 — `IProcessRunner`/`IAgentRunner` fakes drive all core-logic
tests (`CmdFileTests`, `PromptScannerTests`, `PlnFileTests`, `QaFileTests`, `RstFileTests`,
`ConventionLoaderTests`, extended `TaskRunnerTests` covering the plan/pause/resume gate). No new
test infrastructure or dependency is needed.

**Rationale**: The constitution's core-logic-tests-mandatory rule and v0.1's existing fakes already
cover the shape of what's needed; the plan phase is just one more `IAgentRunner.RunAsync` call in
the same loop, testable the same way retries already are.
