using System.Text.Json;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Evidence;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>
/// The Run card (ontwerp-v0.3.md "The Run card" / "API"): every status read and every button is a
/// shell-out to mf-run via IRunClient — this class holds zero PID or process-tree logic of its own.
/// </summary>
public static class RunEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static void MapRunApi(this IEndpointRouteBuilder app)
    {
        // One mf-run spawn (a fresh .NET process) plus a port probe per call, re-read by every open
        // page on every SSE "run" event — memoized per project for a few seconds
        // (docs/prompts/0010). Every action below invalidates its own project first thing, so the
        // status read a button triggers after itself always reflects the action.
        app.MapGet("/api/projects/{name}/run", async (string name, ProjectRegistry registry, IRunClient runClient,
            TtlCache<RunStatusDto> cache) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            return Results.Ok(await cache.GetOrAddAsync(project.Name, () => ReadStatusAsync(project.Path, runClient)));
        });

        app.MapPost("/api/projects/{name}/run/start", (string name, ProjectRegistry registry, IRunClient runClient, TtlCache<RunStatusDto> cache) =>
            RunAllAsync(name, registry, runClient.StartAsync, cache));

        app.MapPost("/api/projects/{name}/run/stop", (string name, ProjectRegistry registry, IRunClient runClient, TtlCache<RunStatusDto> cache) =>
            RunAllAsync(name, registry, runClient.StopAsync, cache));

        app.MapPost("/api/projects/{name}/run/{service}/start", (string name, string service, ProjectRegistry registry, IRunClient runClient, TtlCache<RunStatusDto> cache) =>
            RunOneAsync(name, service, registry, runClient.StartAsync, cache));

        app.MapPost("/api/projects/{name}/run/{service}/stop", (string name, string service, ProjectRegistry registry, IRunClient runClient, TtlCache<RunStatusDto> cache) =>
            RunOneAsync(name, service, registry, runClient.StopAsync, cache));

        app.MapPost("/api/projects/{name}/run/{service}/restart", (string name, string service, ProjectRegistry registry, IRunClient runClient, TtlCache<RunStatusDto> cache) =>
            RunOneAsync(name, service, registry, runClient.RestartAsync, cache));

        // The stable row's Promote (docs/specs/cockpit/project.md#run): spawned detached and
        // answered at once — a publish takes minutes, far past the mf-run timeout, and the row
        // follows `building` through the normal refetch. Refused while a command is running (the
        // publish would read a tree the agent is editing) and while a promote is already building;
        // the status read for that check is fresh, not the cached one.
        app.MapPost("/api/projects/{name}/run/stable/promote", async (string name, ProjectRegistry registry, IRunClient runClient, TtlCache<RunStatusDto> cache) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            if (!await HasStableAsync(project.Path))
                return Results.NotFound();

            if (LaneScanner.Scan(project.Path).Any(i => i.Cmd?.Status == CmdStatus.Running))
                return Results.Conflict(new { error = "a command is running; promote after it finishes" });

            var status = await ReadStatusAsync(project.Path, runClient);
            if (status.Stable?.State == "building")
                return Results.Conflict(new { error = "a promote is already building" });

            RunActionResponseDto response;
            try
            {
                var pid = runClient.StartPromote(project.Path);
                response = new RunActionResponseDto(true, 0, $"promote started (pid {pid})", false);
            }
            catch (Exception ex)
            {
                response = new RunActionResponseDto(false, -1, $"promote failed to start: {ex.Message}", false);
            }
            cache.Invalidate(project.Name);
            return response.Success
                ? Results.Accepted(value: response)
                : Results.Ok(response);
        });

        // Not called out in ontwerp-v0.3.md's own API list, but required by its "Run card" prose
        // ("an expandable bounded tail of .magnaflow/run/<service>.log — the existing bounded tail
        // reader"). Read-only, so an unrecognized service name just yields Exists=false rather than
        // a 404 — nothing to guard here that TailReader doesn't already handle.
        app.MapGet("/api/projects/{name}/run/{service}/log", (string name, string service, int? tail, ProjectRegistry registry) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            var logPath = Path.Combine(project.Path, ".magnaflow", "run", $"{service}.log");
            var maxLines = tail is > 0 ? tail.Value : TailReader.DefaultMaxLines;
            var result = TailReader.Read(logPath, maxLines);
            return Results.Ok(new LogTailDto(result.Lines, result.Truncated, result.Exists));
        });
    }

    private static async Task<RunStatusDto> ReadStatusAsync(string projectRoot, IRunClient runClient)
    {
        var serviceNames = await ReadConfiguredServiceNamesAsync(projectRoot);
        if (serviceNames is null)
            return new RunStatusDto(false, []);

        var result = await runClient.StatusAsync(projectRoot);

        List<RunServiceStatusDto>? entries = null;
        try { entries = JsonSerializer.Deserialize<List<RunServiceStatusDto>>(result.Output, JsonOpts); }
        catch (JsonException) { /* falls through to the error shape below */ }

        return entries is null
            ? new RunStatusDto(true, [], $"mf-run status failed (exit {result.ExitCode}): {Truncate(result.Output)}")
            : new RunStatusDto(true, entries.Where(e => e.Stable != true).ToList(), Stable: entries.FirstOrDefault(e => e.Stable == true));
    }

    private static async Task<IResult> RunAllAsync(
        string name, ProjectRegistry registry, Func<string, string?, CancellationToken, Task<RunActionResult>> action,
        TtlCache<RunStatusDto> cache)
    {
        var project = ProjectResolver.Find(registry, name);
        if (project is null)
            return Results.NotFound();

        var result = await action(project.Path, null, default);
        cache.Invalidate(project.Name);
        return Results.Ok(ToDto(result));
    }

    private static async Task<IResult> RunOneAsync(
        string name, string service, ProjectRegistry registry, Func<string, string?, CancellationToken, Task<RunActionResult>> action,
        TtlCache<RunStatusDto> cache)
    {
        var project = ProjectResolver.Find(registry, name);
        if (project is null)
            return Results.NotFound();

        var known = service == StableServiceName
            ? await HasStableAsync(project.Path)
            : (await ReadConfiguredServiceNamesAsync(project.Path))?.Contains(service, StringComparer.Ordinal) == true;
        if (!known)
            return Results.NotFound();

        var result = await action(project.Path, service, default);
        cache.Invalidate(project.Name);
        return Results.Ok(ToDto(result));
    }

    /// <summary>mf-run's name for the stable instance (`run.stable`), reserved: no service may use it.</summary>
    private const string StableServiceName = "stable";

    private static async Task<bool> HasStableAsync(string projectRoot)
    {
        var path = ProjectConfigReader.ConfigPath(projectRoot);
        return File.Exists(path) && ProjectConfigReader.HasRunStable(await File.ReadAllTextAsync(path));
    }

    private static async Task<IReadOnlyList<string>?> ReadConfiguredServiceNamesAsync(string projectRoot)
    {
        var path = ProjectConfigReader.ConfigPath(projectRoot);
        if (!File.Exists(path))
            return null;

        var content = await File.ReadAllTextAsync(path);
        return ProjectConfigReader.ReadRunServiceNames(content);
    }

    private static RunActionResponseDto ToDto(RunActionResult result) =>
        new(result.Success, result.ExitCode, result.Output, result.TimedOut);

    private static string Truncate(string s) => s.Length > 500 ? s[..500] + "…" : s;
}
