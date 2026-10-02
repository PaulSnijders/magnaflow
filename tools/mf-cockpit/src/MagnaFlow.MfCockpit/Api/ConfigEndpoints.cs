using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Evidence;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>GET /api/config — the sliver of server config the frontend needs: whether to link to
/// chat.html at all (v0.2), plus the loaded config path and effective run/chat commands so a stale
/// or wrong config is diagnosable from the dashboard
/// (docs/prompts/0001-cmd-cockpit-spawn-errors.md "Dashboard visibility"). GET /api/cockpit/log is
/// the matching bounded tail of the cockpit's own diary (ICockpitLog).</summary>
public static class ConfigEndpoints
{
    public static void MapConfigApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", (CockpitConfig config) =>
            Results.Ok(new CockpitConfigDto(config.Chat.Enabled, config.ConfigPath, config.Run.Command, config.Chat.Command)));

        app.MapGet("/api/cockpit/log", (int? tail, ICockpitLog log) =>
        {
            var maxLines = tail is > 0 ? tail.Value : TailReader.DefaultMaxLines;
            var result = TailReader.Read(log.Path, maxLines);
            return Results.Ok(new LogTailDto(result.Lines, result.Truncated, result.Exists));
        });
    }
}
