---
title: "mf-worker: one commit per branchless run"
status: done
attempts: 1
created: 2026-08-24
---

## Context

A command that runs without complications leaves four commits in the log:
the cmd file (written by hand / by the cockpit), then three from the
worker — `-> running`, the work itself, and `-> done`. On a
single-machine setup the `running` commit has no reader: it was the claim
that keeps a second worker off the task, and there is no second worker.
Its one remaining job is evidence that a run started, for the case where
the run dies before it finishes.

That evidence is worth keeping, but it does not need to survive as its own
commit once the run succeeds — the work commit and the `-> done` commit
happen milliseconds apart with no observer in between.

## Task

1. **Fold a successful branchless run into one commit.** The worker keeps
   writing `status: running` into the cmd frontmatter and committing it
   exactly as it does today. At the end of the run, instead of adding a
   work commit and a status commit, it stages everything (code, spec
   updates, rst, `status: done`, `attempts`, the `.magnaflow/<id>/`
   bookkeeping) and `git commit --amend`s that bookkeeping commit, with
   the message the work commit uses today (`command <id>: <summary>`).
   The net diff of the resulting single commit is `pending -> done` plus
   the change. A run that ends `failed` folds the same way.

2. **Amend only when it is provably safe; otherwise behave as today.**
   Two conditions, both checked immediately before the amend:

   - `HEAD` is still exactly the bookkeeping commit this run wrote. The
     agent may commit on its own, and a retried or resumed run starts
     from a tree the worker did not just commit — in both cases HEAD has
     moved and there is nothing to amend.
   - that commit is not reachable from any remote-tracking ref, so the
     amend can never turn into a force-push.

   If either fails, fall back to the current behaviour (separate work and
   status commits) without erroring — this is a tidier log, not a
   correctness feature. If the worker currently pushes right after the
   `running` commit, move that push to the end of the run; nothing may be
   pushed between the bookkeeping commit and its amend.

3. **Branch mode is unchanged.** When the cmd carries `branch:`, the
   two-plane rule stands: bookkeeping commits on the base branch, code and
   its spec updates in the work branch. Multiple commits are correct there
   — skip the fold entirely.

4. **Tests** in the existing style: the happy path yields one commit whose
   diff contains both the status transition and the change; HEAD moved by
   the agent falls back to two commits; a bookkeeping commit already on a
   remote-tracking ref falls back to two commits; branch mode still
   produces the split.

Record the rule in `docs/mf-spec/system.md`'s Branches section — one
sentence, in house style: branchless runs land as a single commit,
branched runs keep the split.

## Not in scope

- The cmd commit itself. That is written before the worker starts and
  stays separate.
- Any setting to choose between few and many commits.
- Cockpit rendering and lane logic — unchanged.
