using YamlDotNet.Serialization;

namespace MagnaFlow.WorkerController.Execution;

/// <summary>
/// Writes/reads .magnaflow/&lt;NNNN-name&gt;/session.yml — the sole successor to v0.1's
/// result.yml for cross-run session continuity (spec research R4/R6). Controller-owned; the
/// only piece of cross-run bookkeeping permitted in .magnaflow/, because it is runtime evidence
/// (an agent session identifier), not content — status and summaries now live in docs/prompts/.
/// </summary>
public static class SessionEvidence
{
    public static string SessionPath(string evidenceDirectory) => Path.Combine(evidenceDirectory, "session.yml");

    /// <summary>Writes the session ID a run ended with (paused or terminal); does nothing when null.</summary>
    public static void Write(string evidenceDirectory, string? sessionId)
    {
        if (sessionId is null)
            return;

        Directory.CreateDirectory(evidenceDirectory);
        var serializer = new SerializerBuilder().Build();
        File.WriteAllText(SessionPath(evidenceDirectory), serializer.Serialize(new Dictionary<string, object>
        {
            ["session"] = sessionId,
        }));
    }

    /// <summary>Reads the recorded agent session ID; null when absent (no prior session, or the agent never reported one).</summary>
    public static string? ReadSessionId(string evidenceDirectory)
    {
        var path = SessionPath(evidenceDirectory);
        if (!File.Exists(path))
            return null;
        var yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build()
            .Deserialize<Dictionary<string, string?>>(File.ReadAllText(path));
        return yaml.TryGetValue("session", out var session) && !string.IsNullOrWhiteSpace(session)
            ? session
            : null;
    }
}
