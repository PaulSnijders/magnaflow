using System.Text.Json;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Chat;

/// <summary>
/// Drives `claude -p --output-format stream-json --verbose --permission-mode plan`, mirroring
/// MagnaFlow.WorkerController.Agents.ClaudeCodeRunner's stream-json harvesting. Read-only by
/// construction (ontwerp-v0.1.md "The chat"): `--permission-mode plan` is always first in argv and
/// is never something chat.args in config can remove — config.Chat.Args can only append, i.e.
/// narrow further, never widen.
/// </summary>
public sealed class ClaudeChatRunner(IProcessRunner processRunner, ChatConfig config) : IChatRunner
{
    public async Task<ChatRunResult> RunAsync(
        string projectRoot,
        string message,
        string? resumeSessionId,
        Action<string> onRawOutputLine,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--permission-mode", "plan" };
        if (resumeSessionId is not null)
        {
            args.Add("--resume");
            args.Add(resumeSessionId);
        }
        args.AddRange(config.Args);

        string? sessionId = resumeSessionId;
        string? finalText = null;

        void OnLine(string line)
        {
            onRawOutputLine(line);
            TryHarvest(line, ref sessionId, ref finalText);
        }

        var result = await processRunner.RunExecutableAsync(
            config.Command, args, projectRoot, OnLine, config.Timeout, standardInput: message, cancellationToken);

        return new ChatRunResult(result.ExitCode, sessionId, finalText, result.TimedOut);
    }

    // Mirrors MagnaFlow.WorkerController.Agents.ClaudeCodeRunner.TryHarvest: the final human-
    // readable reply is the "result" field of the stream's terminal {"type":"result",...} event,
    // not any concatenation of the raw lines (which include tool-call/system bookkeeping events
    // the chat UI has no business showing).
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
            // Not a JSON line; the client still sees it in the raw stream.
        }
    }
}
