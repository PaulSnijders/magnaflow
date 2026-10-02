using System.Text.Json;
using MagnaFlow.MfCockpit.Live;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>GET /api/events — one SSE stream fanning out project+kind only, no payloads
/// (ontwerp-v0.1.md "Live updates"). Clients re-fetch whatever the kind implies.</summary>
public static class EventsEndpoints
{
    // A heartbeat every ~15 s so the client can tell a live-but-idle stream from a dead socket that
    // still looks open (the Tailscale/laptop-sleep case — ontwerp-v0.5.md item 1). Sent as a real
    // {"kind":"ping"} data frame, not a `:` comment, because EventSource never surfaces comment
    // lines to JS, so a comment can't reset the client's own dead-connection watchdog. The client
    // treats a ping as traffic only (no refetch).
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static void MapEventsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (HttpContext ctx, SseHub hub) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";

            var (id, reader) = hub.Subscribe();
            var ct = ctx.RequestAborted;
            try
            {
                await ctx.Response.Body.FlushAsync(ct);
                while (!ct.IsCancellationRequested)
                {
                    var readTask = reader.WaitToReadAsync(ct).AsTask();
                    var completed = await Task.WhenAny(readTask, Task.Delay(HeartbeatInterval, ct));

                    if (completed == readTask)
                    {
                        if (!await readTask)
                            break; // channel completed
                        while (reader.TryRead(out var evt))
                        {
                            var payload = JsonSerializer.Serialize(new { project = evt.Project, kind = evt.Kind });
                            await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
                        }
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                    else
                    {
                        await ctx.Response.WriteAsync("data: {\"kind\":\"ping\"}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // client disconnected
            }
            finally
            {
                hub.Unsubscribe(id);
            }
        });
    }
}
