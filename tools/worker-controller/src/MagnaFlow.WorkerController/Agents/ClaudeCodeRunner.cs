using System.Text.Json;
using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Infrastructure;

namespace MagnaFlow.WorkerController.Agents;

/// <summary>
/// Drives Claude Code headless: `claude -p --output-format stream-json --verbose` (research R5).
/// The prompt goes in via stdin (command lines have length limits; specs can be large).
/// Every raw output line is streamed verbatim to the caller (the claude.log audit trail);
/// the session id and final response are picked out of the JSON stream along the way.
/// Extra flags (e.g. --dangerously-skip-permissions) come verbatim from config agent.args (FR-008a).
/// </summary>
public sealed class ClaudeCodeRunner(IProcessRunner processRunner, ProjectConfig config) : IAgentRunner
{
    public async Task<bool> IsAvailableAsync(string workingDirectory)
    {
        try
        {
            var result = await processRunner.RunExecutableAsync(
                config.AgentCommand, ["--version"], workingDirectory, timeout: TimeSpan.FromSeconds(60));
            return result.Succeeded;
        }
        catch (Exception)
        {
            return false; // executable not found
        }
    }

    public async Task<AgentResult> RunAsync(
        string prompt,
        string? resumeSessionId,
        string workingDirectory,
        Action<string> onRawOutputLine,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose" };
        if (resumeSessionId is not null)
        {
            args.Add("--resume");
            args.Add(resumeSessionId);
        }
        args.AddRange(config.AgentArgs);

        string? sessionId = resumeSessionId;
        string? finalText = null;

        void OnLine(string line)
        {
            onRawOutputLine(line);
            TryHarvest(line, ref sessionId, ref finalText);
        }

        var result = await processRunner.RunExecutableAsync(
            config.AgentCommand, args, workingDirectory,
            OnLine, config.CommandTimeout, standardInput: prompt, cancellationToken);

        return new AgentResult(result.ExitCode, sessionId, finalText, result.TimedOut);
    }

    private static void TryHarvest(string line, ref string? sessionId, ref string? finalText)
    {
        if (!line.TrimStart().StartsWith('{'))
            return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String)
                sessionId = sid.GetString();
            if (root.TryGetProperty("type", out var type) && type.ValueEquals("result")
                && root.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.String)
                finalText = res.GetString();
        }
        catch (JsonException)
        {
            // Not a JSON line; the raw log still has it.
        }
    }
}
