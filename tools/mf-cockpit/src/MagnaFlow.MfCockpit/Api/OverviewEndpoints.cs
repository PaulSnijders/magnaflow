using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>
/// GET /api/overview — everything the shared page chrome needs in one request
/// (docs/prompts/0010). `Cockpit.wireNav` + `Cockpit.summaryBar` used to open a page with three
/// separate calls (/api/config for the chat link, /api/projects for the switcher,
/// /api/projects/{name} for the bar itself); this serves all three. Deliberately additive: the
/// endpoints it composes stay exactly as they are, for the other pages and for `curl`.
/// </summary>
public static class OverviewEndpoints
{
    public static void MapOverviewApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/overview", (string? project, ProjectRegistry registry, CockpitConfig config, IClock clock) =>
        {
            var names = registry.All.Select(p => p.Name).ToList();

            // An unknown (or absent) ?project= yields project: null rather than a 404 — the nav and
            // the switcher must still render on a page opened with a stale or mistyped name.
            var entry = string.IsNullOrWhiteSpace(project) ? null : ProjectResolver.Find(registry, project);
            var summary = entry is null ? null : ProjectsEndpoints.BuildSummary(entry, clock);

            return Results.Ok(new OverviewDto(config.Chat.Enabled, names, summary));
        });
    }
}
