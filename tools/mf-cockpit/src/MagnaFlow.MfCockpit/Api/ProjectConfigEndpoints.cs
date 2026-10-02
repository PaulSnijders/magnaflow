using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>
/// Write #5 (ontwerp-v0.1.md's invariant list, added by v0.3): editing exactly
/// .magnaflow/config.yml — the endpoint takes no path parameter at all, so there is nothing to
/// escape. GET returns the raw content plus a hash for optimistic concurrency; PUT enforces, in
/// order, hash match (409) -&gt; YAML parses (400) -&gt; size cap (400), then writes + commits
/// (ontwerp-v0.3.md "The Config page" / "Save flow").
/// </summary>
public static class ProjectConfigEndpoints
{
    public const int MaxContentBytes = 256 * 1024;

    public static void MapProjectConfigApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{name}/config", async (string name, ProjectRegistry registry) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            var path = ProjectConfigReader.ConfigPath(project.Path);
            if (!File.Exists(path))
                return Results.NotFound();

            var content = await File.ReadAllTextAsync(path);
            return Results.Ok(new ProjectConfigDto(content, ProjectConfigReader.ComputeHash(content), ProjectConfigReader.Summarize(content)));
        });

        app.MapPut("/api/projects/{name}/config", async (string name, ProjectConfigSaveRequest request, ProjectRegistry registry, IGitClient git) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            var path = ProjectConfigReader.ConfigPath(project.Path);
            if (!File.Exists(path))
                return Results.NotFound();

            var onDisk = await File.ReadAllTextAsync(path);
            if (!string.Equals(ProjectConfigReader.ComputeHash(onDisk), request.BaseHash, StringComparison.Ordinal))
                return Results.Conflict(new { error = "config.yml changed on disk since you loaded it — reload and redo" });

            var (warnings, parseError) = ProjectConfigReader.Validate(request.Content);
            if (parseError is not null)
                return Results.BadRequest(new { error = parseError });

            var contentBytes = System.Text.Encoding.UTF8.GetByteCount(request.Content);
            if (contentBytes > MaxContentBytes)
                return Results.BadRequest(new { error = $"config.yml exceeds the {MaxContentBytes / 1024} KB size cap ({contentBytes} bytes)" });

            await File.WriteAllTextAsync(path, request.Content);
            try
            {
                await git.CommitFileAsync(project.Path, Path.GetRelativePath(project.Path, path), "cockpit: edit config");
            }
            catch (GitException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(new ProjectConfigSaveResponse(ProjectConfigReader.ComputeHash(request.Content), warnings));
        });
    }
}
