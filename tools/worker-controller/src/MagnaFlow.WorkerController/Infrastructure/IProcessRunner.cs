namespace MagnaFlow.WorkerController.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// The single seam to the outside world: every external command (agent, git, build, test)
/// goes through here, so core logic is testable with fakes (research R3).
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
        string? standardInput = null,
        CancellationToken cancellationToken = default);

    /// <summary>Runs a command line through the platform shell (for user-authored build/test commands).</summary>
    Task<ProcessResult> RunShellAsync(
        string commandLine,
        string workingDirectory,
        Action<string>? onOutputLine = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
