---
title: "mf-worker: the terminal commit must survive evidence staging"
status: ready
created: 2026-09-10
---

## Context

Twice now on `wozzol2` a run has finished its work and left nothing but
its claim commit behind:

```
751b83a 0048: ask for the store review ...      <- Paul, by hand, 14:10
983619a mf-worker: 0048-app-rate-... -> running <- the worker, 13:46
28ebb9e 0048                                    <- the cmd file
eb8d8e0 0047 results                            <- Paul, by hand, 3 days later
15aecab mf-worker: 0047-machineleesbare-metadata -> running
```

The run itself was fine. On disk, `0048-cmd-...md` reads `status: done`
with `attempts: 1`, `0048-rst-...md` is written in full, and the code
change is in the tree — none of it committed, none of it pushed. The
worker never got to its terminal commit. The next run then refuses at
the clean-tree gate, so the lane stalls until a human commits by hand,
which is what both `751b83a` and `eb8d8e0` are.

**Where it dies.** `TaskRunner.RunAsync`, terminal block:

```csharp
await git.StagePathAsync(cmdPathRelative);
if (File.Exists(cmd.PlnPath)) await git.StagePathAsync(RelPath(cmd.PlnPath));
await git.StagePathAsync(RelPath(cmd.RstPath));
await git.StagePathAsync(evidenceDirRelative);   // <- throws here
if (fold) await git.AmendCommitAsync(workCommitMessage);
else      await git.CommitAsync($"mf-worker: {cmd.Id} -> ...");
await PushToRemoteAsync(invokingBranch);
```

`GitClient.StagePathAsync` runs `git add -- <path>` through `GitAsync`,
which throws `GitException` on any non-zero exit. Nothing between there
and the commit catches it; `CommandInfrastructure.GuardEnvironmentAsync`
catches it at the very top, prints `git operation failed: ...` and
returns exit 4. Everything that was staged stays staged, the commit
never happens, the push never happens — after a 25-minute run, in one
grey line at the bottom of the scroll.

**Two ways that `git add` fails, both live on wozzol2 today.**

1. *The path does not exist.* `.magnaflow/<id>/` is created by
   `SessionEvidence.Write`, which returns early when the session id is
   null. wozzol2 has an evidence folder for every command up to 0046 and
   none for 0047 or 0048. `git add -- .magnaflow/0048-app-rate-...`
   then fails with `fatal: pathspec ... did not match any files`
   (exit 128).

2. *The path is ignored.* Since 7 sep 15:10 (`ad9f725`, "chore: untrack
   .magnaflow run evidence") wozzol2's `.gitignore` carries
   `.magnaflow/*` plus `!.magnaflow/config.yml`. `git add` on an
   explicitly named ignored path refuses: *"The following paths are
   ignored by one of your .gitignore files"*, exit 1. The dates line up
   exactly — 0046 ran at 13:43 that day and committed normally; 0047 at
   16:31 was the first run after the ignore rule landed, and the first
   to lose its terminal commit.

Either way the shape of the bug is the same, and it is the wrong way
round: staging the evidence folder is the least valuable thing in that
block, and it is currently the only thing that can stop the run's one
durable output from being written. The same three lines sit in the
`stopRefused` and `paused` blocks, so those paths lose their commit the
same way.

## Task

1. **A tolerant stage for bookkeeping evidence.** Add
   `StageIfPresentAsync(string relativePath)` to `IGitClient` /
   `GitClient` (`tools/worker-controller/src/MagnaFlow.WorkerController/
   Infrastructure/GitClient.cs`). It returns quietly, staging nothing,
   when the path does not exist on disk or when
   `git check-ignore -q -- <path>` reports the project ignores it.
   Anything else still throws `GitException` — this is a narrowing, not
   a blanket `try/catch`.

2. **Use it for `evidenceDirRelative`** in all three blocks of
   `Execution/TaskRunner.cs` that stage it before committing:
   `stopRefused`, `paused` and the terminal block. The cmd, pln and rst
   files keep the strict `StagePathAsync` — those are the command's
   content, and a repo that cannot stage them is genuinely broken.

3. **Say it out loud when the terminal commit is lost.** If the
   terminal commit still cannot be made for some other reason, the run
   must not end on a generic `git operation failed`. Report, on stderr:
   which command it was, that its status and rst are written but
   uncommitted, and that the next run will refuse on a dirty tree until
   someone commits. A run that did its work and could not record it is
   the worst outcome the worker has; it should read that way.

4. **Tests** (`tests/MagnaFlow.WorkerController.Tests/
   TaskRunnerTests.cs`, `FakeGitClient` already has a `StagePathAsync`
   hook to fail a chosen path):

   - a successful branchless run whose evidence folder does not exist
     still lands its single folded commit and still pushes;
   - a successful run whose evidence path is ignored by the project
     does the same;
   - the paused run and the stop-refused run keep their commit under
     both conditions;
   - `StageIfPresentAsync` stages normally when the path exists and is
     not ignored — the existing evidence-staging behaviour must not
     quietly disappear for projects that do track it.

   The 0011 fold tests stay as they are and must keep passing.

## Verify live

Run one small ready command on `wozzol2` end to end — that repo already
ignores `.magnaflow/*`, so it reproduces the bug as it stands. Expected:
one commit containing the status transition and the change, `origin/main`
moved, clean tree afterwards.

Note before testing: the installed binary looks older than this source.
`.magnaflow/0046-.../` on wozzol2 holds only `session.yml` — no
`claude.log`, `build.log` or `test.log` — and `.magnaflow/run/web.pid`
has not been touched since 14 July although `config.yml` lists two run
services, so `mf-run stop`/`start` never fired on those runs. Re-run
`tools/install/install.ps1` after the fix, or the fix will sit in the
repo while `C:\Tools\MagnaFlow\mf-worker\mf-worker.exe` keeps failing
the same way.

## Not in scope

Why the agent session id came back null for 0047 and 0048, so that no
`session.yml` was written at all — that silently degrades self-resume
and deserves its own look; this command only makes the commit survive
it. `RunMfRunAsync`'s missing timeout and mf-run's start/stop behaviour.
Moving evidence somewhere else, or un-ignoring `.magnaflow` in wozzol2.
Recovering 0047 and 0048 — both are already committed by hand.
