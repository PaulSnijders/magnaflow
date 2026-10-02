namespace MagnaFlow.WorkerController;

/// <summary>
/// Stable exit-code vocabulary for composing tools (contracts/cli.md).
/// </summary>
public static class ExitCodes
{
    /// <summary>Requested work succeeded, or a legitimate pause occurred (command done or paused
    /// at questions; batch done; status shown; nothing ready). Pausing for a genuine human
    /// decision is normal controller behavior, not a failure (spec research R8).</summary>
    public const int Success = 0;

    /// <summary>Executed but the command (or at least one command in a batch) ended aborted
    /// (retries exhausted, human abandonment, or an unrecoverable environment error — spec FR-018).</summary>
    public const int TaskFailed = 1;

    /// <summary>Usage or configuration error (unknown command, invalid config, missing spec/base branch).</summary>
    public const int UsageError = 2;

    /// <summary>Precondition refusal (dirty working tree, command not ready). Nothing was mutated.</summary>
    public const int PreconditionRefused = 3;

    /// <summary>Environment error (git or agent executable unavailable).</summary>
    public const int EnvironmentError = 4;
}
