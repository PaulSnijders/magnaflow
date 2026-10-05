using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// Chat end-to-end against a stub agent executable, the same swap-the-command mechanism the
/// worker's and mf-watch's own validation used (ontwerp-v0.1.md's own requirement): chat.command
/// points at fixtures/chat-stub.cmd, which echoes a fixed stream-json reply with a session_id.
/// </summary>
public class ChatE2ETests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;

    public ChatE2ETests()
    {
        _project.WriteCmd("0001-hello", status: "ready");
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
    }

    public void Dispose()
    {
        _project.Dispose();
        try { File.Delete(_configPath); } catch { /* best effort */ }
    }

    private static string StubPath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public async Task Chat_streams_lines_and_reuses_session_id_on_the_second_message()
    {
        if (!OperatingSystem.IsWindows()) return; // fixtures are .cmd stubs
        CockpitFactory.WriteConfig(_configPath, (StubPath("chat-stub.cmd"), TimeoutMinutes: 1.0), ("proj", _project.Root));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var first = await SendChatAsync(client, "hello", chatId: null);
        Assert.True(first.LineFrames.Count >= 2, "expected more than one streamed line frame — streaming, not one shot");
        Assert.Equal("stub-session-001", first.SessionId);
        Assert.False(first.TimedOut);
        Assert.NotNull(first.ChatId);
        // The "done" frame's finalText is the distilled reply (the stream's terminal "result"
        // event), not a concatenation of every raw line — that's what the chat UI renders.
        Assert.Equal("first reply", first.FinalText);

        var second = await SendChatAsync(client, "again", first.ChatId);
        Assert.Equal("stub-session-001", second.SessionId);
        Assert.Equal("resumed reply", second.FinalText);
        // The stub echoes back whatever --resume value it received — confirming the cockpit
        // actually passed the first call's session id into the second call's argv.
        Assert.Contains(second.LineFrames, l => l.Contains("\"resumed_from\":\"stub-session-001\""));
    }

    [Fact]
    public async Task Chat_timeout_kills_a_hanging_stub()
    {
        if (!OperatingSystem.IsWindows()) return; // fixtures are .cmd stubs
        CockpitFactory.WriteConfig(_configPath, (StubPath("chat-stub-hang.cmd"), TimeoutMinutes: 0.05), ("proj", _project.Root));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var result = await SendChatAsync(client, "hello", chatId: null, overallTimeout: TimeSpan.FromSeconds(30));
        stopwatch.Stop();

        Assert.True(result.TimedOut);
        // 0.05 min = 3s configured timeout; the stub sleeps ~1 hour — completing well under that
        // proves the process was actually killed, not merely outlasted.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"took {stopwatch.Elapsed} — timeout did not kill the process promptly");
    }

    private sealed record ChatResult(string? ChatId, string? SessionId, string? FinalText, bool TimedOut, List<string> LineFrames);

    private static async Task<ChatResult> SendChatAsync(HttpClient client, string message, string? chatId, TimeSpan? overallTimeout = null)
    {
        using var cts = new CancellationTokenSource(overallTimeout ?? TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/projects/proj/chat")
        {
            Content = JsonContent.Create(new { message, chatId }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        string? chatIdOut = null;
        string? sessionId = null;
        string? finalText = null;
        var timedOut = false;
        var lineFrames = new List<string>();

        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            if (!line.StartsWith("data: "))
                continue;
            using var doc = JsonDocument.Parse(line["data: ".Length..]);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "start":
                    chatIdOut = root.GetProperty("chatId").GetString();
                    break;
                case "line":
                    lineFrames.Add(root.GetProperty("text").GetString() ?? "");
                    break;
                case "done":
                    sessionId = root.TryGetProperty("sessionId", out var sid) && sid.ValueKind == JsonValueKind.String ? sid.GetString() : null;
                    finalText = root.TryGetProperty("finalText", out var ft) && ft.ValueKind == JsonValueKind.String ? ft.GetString() : null;
                    timedOut = root.GetProperty("timedOut").GetBoolean();
                    return new ChatResult(chatIdOut, sessionId, finalText, timedOut, lineFrames);
            }
        }

        return new ChatResult(chatIdOut, sessionId, finalText, timedOut, lineFrames);
    }
}
