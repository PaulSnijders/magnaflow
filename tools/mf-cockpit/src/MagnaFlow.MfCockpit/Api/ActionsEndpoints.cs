using MagnaFlow.MfCockpit.Actions;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Evidence;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Watch;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>
/// The Actions card (docs/specs/cockpit/project.md#actions): the list, one action's log tail, and
/// Run. Run executes nothing: it writes `.magnaflow/actions/&lt;name&gt;.request` plus the wake file,
/// and the project's watcher runs it inside its poll (docs/specs/concepts/actions.md). An action
/// name becomes a file path, so it is only ever taken from the parsed actions.yml, never from the
/// URL as given.
/// </summary>
public static class ActionsEndpoints
{
    public const int LogTailLines = 100;

    public static void MapActionsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{name}/actions", async (string name, ProjectRegistry registry, IGitClient git) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            if (!File.Exists(ActionsFileLite.FilePath(project.Path)))
                return Results.Ok(new ActionsDto(false, null, false, []));

            var ignored = await git.IsIgnoredAsync(project.Path, ".magnaflow/actions.yml");
            var (actions, error) = ActionsFileLite.Load(project.Path);
            var rows = (actions ?? []).Select(a =>
            {
                var last = ActionsFileLite.LoadLast(project.Path, a.Name);
                return new ActionDto(a.Name, a.Description, a.Kind, a.Schedule,
                    last is null ? null : new ActionLastDto(last.RunAt, last.Trigger, last.Result, last.DurationSeconds, last.RefusedAt, last.Refused),
                    File.Exists(ActionsFileLite.RequestPath(project.Path, a.Name)));
            }).ToList();
            return Results.Ok(new ActionsDto(true, error, ignored, rows));
        });

        app.MapGet("/api/projects/{name}/actions/{action}/log", (string name, string action, ProjectRegistry registry) =>
        {
            var project = ProjectResolver.Find(registry, name);
            var known = project is null ? null : Known(project.Path, action);
            if (known is null)
                return Results.NotFound();

            var tail = TailReader.Read(ActionsFileLite.LogPath(project!.Path, known), LogTailLines);
            return Results.Ok(new LogTailDto(tail.Lines, tail.Truncated, tail.Exists));
        });

        app.MapPost("/api/projects/{name}/actions/{action}/run", async (string name, string action, ProjectRegistry registry, IWatchControl watchControl) =>
        {
            var project = ProjectResolver.Find(registry, name);
            var known = project is null ? null : Known(project.Path, action);
            if (known is null)
                return Results.NotFound();

            // Nothing would consume the request without a watcher; a pending one already asks.
            if (!await watchControl.IsRunningAsync(project!.Path))
                return Results.Conflict(new WatchActionResponseDto(false, "no watcher runs for this project; start it first"));
            var request = ActionsFileLite.RequestPath(project.Path, known);
            if (File.Exists(request))
                return Results.Conflict(new WatchActionResponseDto(false, $"a run of '{known}' is already queued"));

            try
            {
                Directory.CreateDirectory(ActionsFileLite.Directory(project.Path));
                File.WriteAllText(request, "");
                MfWatchWakeFile.Write(project.Path);
                return Results.Ok(new WatchActionResponseDto(true, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.Ok(new WatchActionResponseDto(false, ex.Message));
            }
        });
    }

    /// <summary>The name exactly as it appears in a parsed actions.yml, or null.</summary>
    private static string? Known(string projectRoot, string action)
    {
        var (actions, _) = ActionsFileLite.Load(projectRoot);
        return actions?.FirstOrDefault(a => a.Name == action)?.Name;
    }
}
