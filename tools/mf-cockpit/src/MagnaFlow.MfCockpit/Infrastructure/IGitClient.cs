namespace MagnaFlow.MfCockpit.Infrastructure;

public sealed record GitCommit(string Hash, string Author, string Date, string Message);

/// <summary>Ahead/Behind count against the upstream as last fetched (`rev-list --left-right
/// --count HEAD...@{u}`, local only); both null without an upstream. Both non-zero is the diverged
/// state the Git card offers Sync for (docs/prompts/0025).</summary>
public sealed record GitInfo(bool Available, string? Branch, bool Dirty, IReadOnlyList<GitCommit> Commits, IReadOnlyList<string> ChangedFiles,
    int? Ahead = null, int? Behind = null);

/// <summary>The exit code plus captured stdout+stderr of exactly one git invocation, handed back
/// rather than interpreted — write #8's `git pull --ff-only` (ontwerp-v0.5.md item 7) and write
/// #10's fetch/switch (docs/prompts/0014) all render inline on the card. A non-zero exit ("not
/// possible to fast-forward", "is not a commit and a branch cannot be created from it") is
/// information to show, not an error to swallow.</summary>
public sealed record GitCommandResult(int ExitCode, string Output, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>One entry of the branch dropdown (write #10, docs/prompts/0014). Name is what you would
/// type after `git switch`: for a branch that exists only as `origin/&lt;name&gt;` here, that is the
/// short name — `git switch` creates the local tracking branch from it, which is exactly the worker
/// case. RemoteOnly is only there so the UI can say so.</summary>
/// <summary>Sync's outcome (docs/prompts/0025): Pull is the `git pull --rebase` itself; Conflicts is
/// non-empty when the rebase stopped and was aborted (the tree is back where it was); Push is the
/// `git push` that followed a successful pull, null when the pull failed or there is no remote.</summary>
public sealed record GitSyncResult(GitCommandResult Pull, IReadOnlyList<string> Conflicts, GitCommandResult? Push);

public sealed record GitBranch(string Name, bool RemoteOnly);

/// <summary>Current is `git rev-parse --abbrev-ref HEAD` ("HEAD" on a detached head, which the
/// cockpit never creates); Branches is empty when git isn't available, which fails the checkout
/// whitelist closed.</summary>
public sealed record GitBranches(string? Current, IReadOnlyList<GitBranch> Branches);

/// <summary>
/// Read-only git info (branch/dirty/log — ontwerp-v0.1.md "What it shows") plus the cockpit's own
/// writes' immediate follow-up commit (ontwerp-v0.1.md "Invariant: buttons, not an actor" —
/// "every write is committed immediately"). No branch/remote decisions ever, unlike the worker
/// controller — same restraint as mf-watch's own IGitClient: which branch the working copy is on
/// is a human's choice, and write #10 only carries that choice out, it never picks one.
/// </summary>
public interface IGitClient
{
    Task<GitInfo> GetInfoAsync(string projectRoot, int logCount = 10);

    /// <summary>Stages exactly one file and commits it. Throws GitException on failure — callers
    /// decide whether that should surface as a 500 (the draft file itself still exists on disk;
    /// only the commit failed).</summary>
    Task CommitFileAsync(string projectRoot, string relativePath, string message);

    /// <summary>Write #4: "commit all" — the same `git add -A &amp;&amp; git commit -m "..."` a human
    /// would type by hand over whatever is currently pending, hand-edits included. Returns false
    /// (no-op) when nothing was pending; throws GitException on failure.</summary>
    Task<bool> CommitAllAsync(string projectRoot, string message);

    /// <summary>Write #4's second half: exactly `git push` right after a successful commit-all.
    /// Returns null when the repo has no remote at all (a local-only project — nothing to push to,
    /// not an error). Never throws for a git-level failure: the commit already happened, so a
    /// rejected push ("no upstream", "fetch first") is rendered inline, not swallowed.</summary>
    Task<GitCommandResult?> PushAsync(string projectRoot);

    /// <summary>Write #8: exactly `git pull --ff-only`, nothing else — no merge, rebase, stash or
    /// autocommit (ontwerp-v0.5.md item 7). Never throws for a git-level failure: the caller renders
    /// the exit code and captured output inline, so "not possible to fast-forward" is shown, not
    /// swallowed. The endpoint enforces the dirty-tree and running-command guards before calling.</summary>
    Task<GitCommandResult> PullFastForwardAsync(string projectRoot);

    /// <summary>Sync (docs/prompts/0025): `git pull --rebase`, then `git push` — for a working copy
    /// that is both ahead of and behind its upstream, which `--ff-only` refuses. Only local,
    /// unpushed commits are replayed; never a merge commit, never a force-push. A rebase that stops
    /// on a conflict is aborted (`git rebase --abort`) and its files reported. Never throws for a
    /// git-level failure; the endpoint enforces the same guards as the pull.</summary>
    Task<GitSyncResult> SyncRebaseAsync(string projectRoot);

    /// <summary>Write #10: exactly `git fetch --quiet`. The one network call in the branch flow, and
    /// therefore an explicit human action (the Git card's refresh control) — never on page load and
    /// never on opening the dropdown. Never throws: an offline worker's failure is rendered next to
    /// the (still perfectly usable) local list.</summary>
    Task<GitCommandResult> FetchAsync(string projectRoot);

    /// <summary>Write #10: local branches plus remote-tracking refs, `&lt;remote&gt;/HEAD` excluded and a
    /// local branch collapsed with its remote counterpart. Purely local — it reads refs already on
    /// disk, so it costs about what `rev-parse` does and is deliberately not cached; use
    /// <see cref="FetchAsync"/> first to see branches this copy has never heard of.</summary>
    Task<GitBranches> ListBranchesAsync(string projectRoot);

    /// <summary>Write #10: exactly `git switch &lt;branch&gt;` — not `git checkout`, because for a name
    /// that exists only as `origin/&lt;name&gt;` here `switch` creates the local tracking branch by
    /// itself (the worker case; a bare `checkout` would fail there), and because it cannot silently
    /// detach HEAD on a tag or a commit. Never throws: git's own refusal — an ambiguous name across
    /// several remotes, say — is passed back verbatim. The endpoint whitelists the branch against
    /// <see cref="ListBranchesAsync"/> and enforces the dirty-tree and running-command guards
    /// before calling.</summary>
    Task<GitCommandResult> SwitchAsync(string projectRoot, string branch);
}

public sealed class GitException(string message) : Exception(message);
