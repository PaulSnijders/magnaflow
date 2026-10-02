namespace MagnaFlow.MfCockpit.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// The single seam to the outside world: git and the chat agent process both go through here, so
/// core logic is testable with a fake (same role as the worker controller's and mf-watch's own
/// IProcessRunner; duplicated rather than referenced — no library coupling, ontwerp-v0.1.md "Tech").
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
}
