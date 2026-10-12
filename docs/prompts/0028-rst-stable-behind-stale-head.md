---
title: "Cockpit stable row: Behind reads HEAD with the run data, not from the Git card"
cmd: 0028-cmd-stable-behind-stale-head.md
done: 2026-10-12
summary: "The stable row's behind mark now compares against a HEAD that comes with the run response, so it updates without a page reload. To finish, roll out with install.ps1 and check on triferto-forecast by hand."
---

# Result

Done. `dotnet test` for mf-cockpit: 339 passed, 0 failed. `spec_lint`: 0 problems.

## What changed

- **`IGitClient.GetHeadShaAsync`** (new) is implemented in `GitClient` as one
  `git --no-pager rev-parse HEAD` call. It returns `null` and never throws for
  any of these: a non-zero exit, a timeout, empty output, or an exception from
  the spawn.
- **`RunStatusDto.Head`** is a new last field. It is nullable and has
  `JsonIgnore(WhenWritingNull)`, so a response without it serializes exactly as
  before.
- **`GET /api/projects/{name}/run`** reads the status through the 3-second
  `TtlCache`, unchanged. Only when `status.Stable` is non-null does it add
  `Head` with `with { Head = ... }`. The cached value never holds a head, so
  the head is read fresh on every request. Without a stable entry, git is
  never spawned.
- **`project.html`**:
  - `gitHead` is gone, and `loadGit()` no longer re-renders the stable row.
  - `renderRunServices` and `stableRowHtml` take the head from the same run
    response (`status.head`).
  - `rerenderStable()` stays, because the lane load still calls it for
    `laneRunning` (Promote disabled). It passes `lastRunStatus.head`.
  - The Git card does not use the head, and nothing else on the page did.
- **Spec**: `project.md#live-updates` gets one sentence: the HEAD for Behind
  is read on every refetch, outside the cache. `#run` already described the
  behavior.

## Tests

- In `RunApiIntegrationTests`, which now registers a `FakeGitClient` with new
  `HeadSha` and `HeadCalls` members:
  - With stable configured, the response carries the head.
  - The head is fresh after a cache hit: `StatusCalls == 1` but the second
    response has the new head.
  - A failed read (`null`) gives no `head` property, no `error`, and the
    stable entry is still there.
  - Without stable there is no `head` and no `stable` in the raw JSON, and
    `HeadCalls == 0`.
- `GitClientHeadTests` (new) checks the exact arguments, trimming, three
  failure shapes (exit 128, timeout, blank output), and a throwing spawn.

## Verification done

I ran it in headless Chrome over CDP against a scratch git project with
`run.stable`: a trivial publish script and an app that is just `sleep`. I used
the dev build and the installed `mf-run`.

1. After loading: `ready f2b33e6`, no mark.
2. After a new commit plus a run event: `behind` appeared without a reload. A
   commit by itself raises no run event, so I appended to
   `.magnaflow/run/stable.log` to trigger the refetch.
3. After `mf-run promote`: `ready 2cd073f`, and `behind` disappeared without a
   reload.

I cleaned up the scratch dir, the cockpit, Chrome and the stable process
afterwards.

## Not done / for the next session

- The triferto-forecast check is still open. It needs `tools/install/install.ps1`
  and is done by hand, as the cmd says.
- A commit made outside a worker run raises no `run` event, so **behind**
  appears on the next run refetch, not at commit time. The cmd's wording
  ("the next `run` refetch") accepts this. In the worker flow, the promote's
  `state.yml` write is the event that clears it.

## Self-answered questions

- **Which "git/process seam"?** A new method on `IGitClient`, rather than
  calling `IProcessRunner` from the endpoint. Every other git read goes
  through `IGitClient`, and `FakeGitClient` already exists for endpoint tests.
- **Cache, or drop it with the stable entry?** Keep the head out of the cache
  and add it per request. One local `rev-parse` costs about as much as the
  uncached branch read, and this way a commit is visible at once instead of
  after the cache expires.
- **Field name:** `head`, the word the spec uses.
- **How to decide that `run.stable` is set without an extra config read:**
  use `status.Stable is not null`. mf-run only reports a stable entry when
  `run.stable` is configured. If mf-run fails or its output doesn't parse,
  there is no stable entry, so there is no head and no git spawn. That's
  fine, because there is no row to mark.
