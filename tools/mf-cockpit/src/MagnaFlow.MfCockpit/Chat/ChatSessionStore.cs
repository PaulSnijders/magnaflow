using System.Collections.Concurrent;

namespace MagnaFlow.MfCockpit.Chat;

/// <summary>
/// Per-browser-chat continuity (ontwerp-v0.1.md "The chat": "the returned session_id... is kept
/// per browser chat and resumed on the next message"). Keyed by (project, chatId) — chatId is a
/// client-generated id carried in the request body; v0.1 has no auth, so in-memory for the
/// process lifetime is enough (same scope as mf-watch's in-memory stale-notification set).
/// </summary>
public sealed class ChatSessionStore
{
    private readonly ConcurrentDictionary<(string Project, string ChatId), string?> _sessions = new();

    public string? GetSessionId(string project, string chatId) =>
        _sessions.TryGetValue((project, chatId), out var sessionId) ? sessionId : null;

    public void SetSessionId(string project, string chatId, string? sessionId) =>
        _sessions[(project, chatId)] = sessionId;
}
