namespace MagnaFlow.WorkerController.Agents;

public sealed record AgentResult(int ExitCode, string? SessionId, string? FinalText, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// The replaceable AI executor (constitution V / FR-019). The controller only knows:
/// send a prompt, optionally resuming a session; stream raw output; get back a session id
/// and the final response text.
/// </summary>
public interface IAgentRunner
{
    Task<bool> IsAvailableAsync(string workingDirectory);

    Task<AgentResult> RunAsync(
        string prompt,
        string? resumeSessionId,
        string workingDirectory,
        Action<string> onRawOutputLine,
        CancellationToken cancellationToken = default);
}
