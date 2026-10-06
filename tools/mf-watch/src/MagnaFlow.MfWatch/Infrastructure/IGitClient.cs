namespace MagnaFlow.MfWatch.Infrastructure;

public class GitException(string message) : Exception(message);

/// <summary>A `git pull --rebase` stopped on a conflict. The rebase has already been aborted (the
/// tree is back where it was) by the time this is thrown; Files are what conflicted.</summary>
public sealed class GitConflictException(IReadOnlyList<string> files, string message) : GitException(message)
{
    public IReadOnlyList<string> Files { get; } = files;
}

public enum PushOutcome
{
    Pushed,
    /// <summary>The remote has commits this copy lacks ("fetch first" / "non-fast-forward") — the
    /// one push failure a rebase-pull can heal. Every other push failure throws.</summary>
    RejectedNonFastForward,
}

/// <summary>
/// mf-watch only ever needs pull/push (ontwerp-v0.1.md "Local vs remote"): the working copy,
/// branch, and remote are all whatever the human already set up on this machine — mf-watch does
/// not choose branches or remotes the way the worker controller does for work branches.
/// </summary>
public interface IGitClient
{
    Task<bool> IsAvailableAsync();

    /// <summary>`git pull --rebase` on the current branch: only local, unpushed commits are ever
    /// replayed (docs/prompts/0025). Returns true if HEAD moved (activity). Throws
    /// <see cref="GitConflictException"/> after aborting a rebase that stopped on a conflict, and
    /// <see cref="GitException"/> for any other failure.</summary>
    Task<bool> PullAsync();

    /// <summary>Plain `git push` — never forced.</summary>
    Task<PushOutcome> PushAsync();

    /// <summary>Local HEAD and upstream HEAD, as one comparable string — what "the same divergence
    /// persists" means for notify-once. Best effort: never throws.</summary>
    Task<string> SyncStateAsync();
}
