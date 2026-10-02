---
title: "mf-worker: one commit per branchless run"
cmd: 0011-cmd-worker-one-commit-per-run.md
done: 2026-08-24
summary: A branchless run now lands as a single commit — the `-> running` claim commit is amended at the end to carry the work, the report and `-> done` — falling back to today's split whenever amending would be unsafe. Branch mode is untouched.
---

## What was done

**1. Three git primitives.** `IGitClient`/`GitClient` gained
`GetHeadCommitAsync` (`rev-parse HEAD`), `IsOnRemoteTrackingRefAsync`
(`branch --remotes --contains <sha>`, non-empty output = reachable) and
`AmendCommitAsync` (`commit --quiet --amend -m`). A `--contains` query
that fails for any reason answers *yes*: the only thing riding on the
answer is whether to amend, so an unanswerable question must not become
an amend.

**2. The claim commit is remembered.** Right after
`mf-worker: <id> -> running` the runner records HEAD in `claimCommit`
— only in branchless mode, so branch mode pays for no extra git call.

**3. The work commit is deferred, not moved.** In branchless mode the
work plane still runs `git add -A -- . :(exclude).magnaflow
:(exclude)docs/prompts` at exactly the same point as before, but no
longer commits: the staged work rides along to the terminal commit.
Branch mode is byte-for-byte unchanged — commit, push the work branch,
check the invoking branch back out.

**4. The terminal commit folds or falls back.** Immediately before the
amend, and nowhere earlier, the runner checks both conditions: HEAD is
still exactly `claimCommit`, and that commit is not reachable from any
remote-tracking ref. Both hold → stage the bookkeeping on top of the
already-staged work and `git commit --amend -m "command <id>: <title>"`,
so the run's whole net diff is `pending -> done` plus the change in one
commit. Either fails → commit the work on its own, then the status
commit, exactly as before. A run that ends `aborted` folds the same way.

**5. No push between the claim commit and its amend.** There was none
to move: the worker already pushed only after the work commit (branch
plane) and after each terminal bookkeeping commit. That is now the only
push on the branchless path, and it happens after the amend.

**6. Tests** (`TaskRunnerTests`, +6, 155 pass). The happy path yields
one commit whose staged paths carry both the cmd file and the agent's
changes; a failing run folds identically; HEAD moved by the agent falls
back to two commits; a claim commit already on a remote-tracking ref
falls back to two commits; branch mode keeps the split and never
amends, with the work commit still holding code only. `FakeGitClient`
grew a real (if tiny) commit log — message plus staged paths, a moving
`Head`, `CommitBehindTheWorkersBack()` — and `FakeAgentRunner` a
one-shot `OnNextRun` hook, which is how the agent-commits-mid-run case
is expressed.

**Verified against real git**, since the fake proves choreography and
not git semantics: seed → `-> running` → stage code → stage cmd/rst →
`commit --amend`. Result is one commit (`app.js | 1 +`, `cmd.md | 2 +-`,
`rst.md | 1 +`), a clean tree, and `branch --remotes --contains` empty
on a remoteless repo.

**7. Docs.** `docs/mf-spec/system.md` "Branches" gained the rule;
`tools/worker-controller/README.md`'s "Running without branches" no
longer says the work lands "as its own commit, separate from the status
commits", and names the fallback.

## Decisions and deviations from the plan

- **The fold also requires that the run reached the work plane.** A
  branchless run whose planning never gated through has nothing to fold
  and already lands as a single commit today; amending it would only
  relabel `mf-worker: <id> -> aborted (2 attempt(s))` as
  `command <id>: <title>`, which claims work that did not happen. Such
  runs keep their own message. Pinned by
  `BranchlessRunThatNeverReachesTheWorkPlane_KeepsItsOwnStatusCommit`.
- **The safety check runs once, immediately before the amend** — as
  specified — which is why the work commit had to be deferred rather
  than the decision hoisted to the work plane. The cost is that in the
  fallback case the work commit now lands after `mf-run start` instead
  of before it. Nothing changes in what it contains: `git add` has
  already snapshotted the index, so files `mf-run start` may create are
  as untracked afterwards as they were before.
- **The `questions` pause and the `mf-run stop` refusal do not fold.**
  Both return before any work plane, and both are pure bookkeeping
  pairs; folding them is a different change and was not asked for.
- **No setting.** As scoped: the behaviour is unconditional in
  branchless mode and unavailable in branch mode.
