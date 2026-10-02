namespace MagnaFlow.MfWatch.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// The single seam to the outside world: git, the mf-worker binary, and notify_command all go
/// through here, so the poll loop is testable with a fake (mirrors MagnaFlow.WorkerController's
/// IProcessRunner; duplicated rather than referenced — mf-watch has no library coupling to the
/// worker controller, process spawn only, per ontwerp-v0.1.md's invariant).
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs an executable with an argument list (no shell interpretation).</summary>
    Task<ProcessResult> RunExecutableAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onOutputLine = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>Runs a command line through the platform shell (for the user-authored notify_command template).</summary>
    Task<ProcessResult> RunShellAsync(
        string commandLine,
        string workingDirectory,
        Action<string>? onOutputLine = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
