using System.Text.Json;
using MagnaFlow.MfCockpit.Chat;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>POST /api/projects/{name}/chat — spawns the chat agent, streams every raw output line
/// to the client as it arrives, and returns the session id for the next message's --resume
/// (ontwerp-v0.1.md "The chat").</summary>
public static class ChatEndpoints
{
    public static void MapChatApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/projects/{name}/chat", async (string name, ChatRequest request, HttpContext ctx,
            CockpitConfig config, ProjectRegistry registry, IChatRunner runner, ChatSessionStore sessions) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (!config.Chat.Enabled)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var chatId = string.IsNullOrWhiteSpace(request.ChatId) ? Guid.NewGuid().ToString("N") : request.ChatId;
            var resumeSessionId = sessions.GetSessionId(name, chatId);

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";

            async Task WriteFrameAsync(object payload)
            {
                var json = JsonSerializer.Serialize(payload);
                await ctx.Response.WriteAsync($"data: {json}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }

            await WriteFrameAsync(new { type = "start", chatId });

            // CliWrapProcessRunner invokes this callback synchronously, one line at a time, under
            // its own lock — blocking here to flush each line to the response is what makes the
            // stream arrive incrementally instead of all at once at process exit.
            void OnLine(string line) => WriteFrameAsync(new { type = "line", text = line }).GetAwaiter().GetResult();

            var result = await runner.RunAsync(project.Path, request.Message, resumeSessionId, OnLine, ctx.RequestAborted);

            if (result.SessionId is not null)
                sessions.SetSessionId(name, chatId, result.SessionId);

            await WriteFrameAsync(new { type = "done", chatId, sessionId = result.SessionId, finalText = result.FinalText, timedOut = result.TimedOut, exitCode = result.ExitCode });
        });
    }
}
