using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Watch;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>GET /api/projects/{name}/watch?tail=200 — mf-watch.log tail, lockfile presence, and
/// the toggle's own accurate liveness check. start/stop are the toggle's only two buttons
/// (Watch/IWatchControl.cs) — this class holds no process/PID logic itself. check-now is the odd
/// one out: a plain file write, so it bypasses IWatchControl entirely.</summary>
public static class WatchEndpoints
{
    public static void MapWatchApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{name}/watch", async (string name, int? tail, ProjectRegistry registry, IWatchControl watchControl) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            var maxLines = tail is > 0 ? tail.Value : Evidence.TailReader.DefaultMaxLines;
            var status = WatchReader.Read(project.Path, maxLines);
            var running = await watchControl.IsRunningAsync(project.Path);
            return Results.Ok(new WatchDto(new LogTailDto(status.Log.Lines, status.Log.Truncated, status.Log.Exists), status.LockPresent, running));
        });

        app.MapPost("/api/projects/{name}/watch/start", (string name, ProjectRegistry registry, IWatchControl watchControl) =>
            RunActionAsync(name, registry, watchControl.StartAsync));

        app.MapPost("/api/projects/{name}/watch/stop", (string name, ProjectRegistry registry, IWatchControl watchControl) =>
            RunActionAsync(name, registry, watchControl.StopAsync));

        // "Check now" — one empty file, no process logic, no platform split (Watch/MfWatchWakeFile.cs).
        // A running watcher consumes it on its next ~1s sleep slice; if none is running the file just
        // sits there until one starts, which is why the button is disabled while it isn't.
        app.MapPost("/api/projects/{name}/watch/check-now", (string name, ProjectRegistry registry) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            try
            {
                MfWatchWakeFile.Write(project.Path);
                return Results.Ok(new WatchActionResponseDto(true, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.Ok(new WatchActionResponseDto(false, ex.Message));
            }
        });
    }

    private static async Task<IResult> RunActionAsync(
        string name, ProjectRegistry registry, Func<string, CancellationToken, Task<WatchControlResult>> action)
    {
        var project = ProjectResolver.Find(registry, name);
        if (project is null)
            return Results.NotFound();

        var result = await action(project.Path, default);
        return Results.Ok(new WatchActionResponseDto(result.Success, result.Error));
    }
}
