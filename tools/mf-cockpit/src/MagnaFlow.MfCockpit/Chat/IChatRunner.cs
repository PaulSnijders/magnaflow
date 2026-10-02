namespace MagnaFlow.MfCockpit.Chat;

public sealed record ChatRunResult(int ExitCode, string? SessionId, string? FinalText, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Spawns headless Claude Code for one chat message (ontwerp-v0.1.md "The chat"). The cockpit's
/// only knowledge of the agent: send a prompt, optionally resuming a session; stream raw output;
/// get back a session id. Swappable for a stub executable in tests, same FR-019 mechanism the
/// worker controller's IAgentRunner and mf-watch's worker.command use.
/// </summary>
public interface IChatRunner
{
    Task<ChatRunResult> RunAsync(
        string projectRoot,
        string message,
        string? resumeSessionId,
        Action<string> onRawOutputLine,
        CancellationToken cancellationToken = default);
}
