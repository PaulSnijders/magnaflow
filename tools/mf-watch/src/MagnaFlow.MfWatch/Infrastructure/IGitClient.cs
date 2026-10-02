namespace MagnaFlow.MfWatch.Infrastructure;

public sealed class GitException(string message) : Exception(message);

/// <summary>
/// mf-watch only ever needs pull/push (ontwerp-v0.1.md "Local vs remote"): the working copy,
/// branch, and remote are all whatever the human already set up on this machine — mf-watch does
/// not choose branches or remotes the way the worker controller does for work branches.
/// </summary>
public interface IGitClient
{
    Task<bool> IsAvailableAsync();

    /// <summary>Pulls the current branch. Returns true if new commits were brought in (activity).</summary>
    Task<bool> PullAsync();

    Task PushAsync();
}
