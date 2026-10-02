using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Live;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Prompts;
using MagnaFlow.MfCockpit.Watch;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>GET /api/projects, GET /api/projects/{name} — summary + counts (ontwerp-v0.1.md "API").
/// Deliberately git-free: no subprocess spawn on this path at all, so it stays fast regardless of
/// how expensive `git status` is on a given project. Git info is GitEndpoints' own, slower,
/// GET /api/projects/{name}/git.</summary>
public static class ProjectsEndpoints
{
    public static void MapProjectsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects", (ProjectRegistry registry, IClock clock) =>
        {
            var summaries = registry.All.Select(p => BuildSummary(p, clock)).ToList();
            return Results.Ok(summaries);
        });

        app.MapGet("/api/projects/{name}", (string name, ProjectRegistry registry, IClock clock) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            return Results.Ok(BuildSummary(project, clock));
        });

        // Write #7: unregister a project (ontwerp-v0.5.md item 4). It removes exactly one entry from
        // magnaflow.yml (a text edit, the mirror of write #6) and drops the in-memory registration +
        // its watcher — the working copy, its git history and its .magnaflow/ are never touched.
        // Guard: 409 while any command is running (unregistering mid-run would hide a live worker).
        // A live mf-watch service is NOT blocked here — it survives the cockpit forgetting the
        // project; the config-page dialog warns about that before confirming.
        app.MapDelete("/api/projects/{name}", async (string name, ProjectRegistry registry, CockpitConfig config,
            IProjectWatcherRegistry watchers, SseHub hub) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            if (ProjectResolver.HasRunningCommand(project))
                return Results.Conflict(new { error = "a command is currently running — unregistering now would hide a live worker; wait until it finishes" });

            await NewProjectEndpoints.ConfigWriteGate.WaitAsync();
            try
            {
                var removed = await MagnaflowYmlAppender.RemoveProjectAsync(config, name);
                switch (removed.Outcome)
                {
                    case MagnaflowYmlAppender.RemoveOutcome.LegacyFileRefused:
                        return Results.BadRequest(new { error = removed.Error });
                    case MagnaflowYmlAppender.RemoveOutcome.FileNotFound:
                    case MagnaflowYmlAppender.RemoveOutcome.VerifyFailed:
                        return Results.Problem(removed.Error, statusCode: StatusCodes.Status500InternalServerError);
                }

                // Registry + watcher drop only after the file edit verified — so a failed edit never
                // leaves the cockpit serving a project no longer in the config.
                registry.Remove(name);
                watchers.Remove(name);
                hub.Broadcast(new LiveEvent(name, "projects"));
                return Results.Ok(new { removed = true });
            }
            finally
            {
                NewProjectEndpoints.ConfigWriteGate.Release();
            }
        });
    }

    /// <summary>Internal so OverviewEndpoints can serve the same summary shape from its own
    /// endpoint — one summary builder, so the two can never disagree.</summary>
    internal static ProjectSummaryDto BuildSummary(ProjectEntry project, IClock clock)
    {
        var exists = Directory.Exists(project.Path);
        var items = exists ? LaneScanner.Scan(project.Path) : [];
        var counts = CommandCounts.From(items);
        var attention = exists ? AttentionRules.Build(project.Path, items, clock) : [];
        var watch = exists ? WatchReader.Read(project.Path, maxLines: 1) : new WatchStatus(new Evidence.TailResult([], false, false), false);
        var latest = LatestOf(items);
        var running = RunningOf(items);

        return new ProjectSummaryDto(project.Name, project.Path, exists, counts, attention, watch.LockPresent, latest, running);
    }

    /// <summary>The running command the item-2 top bar highlights (ontwerp-v0.5.md item 2): the
    /// highest-id command whose status is `running`, or null when nothing is running.</summary>
    private static LatestCommand? RunningOf(IReadOnlyList<Prompts.LaneItem> items)
    {
        var running = items.Where(i => i.Cmd?.Status == Prompts.CmdStatus.Running).ToList();
        if (running.Count == 0)
            return null;
        var latest = running.Aggregate((a, b) => string.CompareOrdinal(a.Id, b.Id) >= 0 ? a : b);
        return new LatestCommand(latest.Id, latest.Cmd!.Title, "running");
    }

    /// <summary>The highest-id lane item (ontwerp-v0.5.md item 6). Scan() already returns the lane
    /// ordered ascending by ordinal id, so the last item is the highest; recomputed defensively via
    /// ordinal max in case a caller passes an unordered list.</summary>
    private static LatestCommand? LatestOf(IReadOnlyList<Prompts.LaneItem> items)
    {
        if (items.Count == 0)
            return null;
        var latest = items.Aggregate((a, b) => string.CompareOrdinal(a.Id, b.Id) >= 0 ? a : b);
        return new LatestCommand(latest.Id, latest.Cmd?.Title ?? latest.Id, latest.Cmd?.Status.ToYaml() ?? "malformed");
    }
}
