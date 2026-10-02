using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Specs;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>GET /api/projects/{name}/specs[/{**path}] — read-only spec tree browse, path-escape
/// rejection (ontwerp-v0.1.md "Guards").</summary>
public static class SpecsEndpoints
{
    public static void MapSpecsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{name}/specs", (string name, ProjectRegistry registry) => Handle(name, null, registry));
        app.MapGet("/api/projects/{name}/specs/{**path}", (string name, string? path, ProjectRegistry registry) => Handle(name, path, registry));
    }

    private static IResult Handle(string name, string? path, ProjectRegistry registry)
    {
        var project = ProjectResolver.Find(registry, name);
        if (project is null)
            return Results.NotFound();

        return SpecsBrowser.Browse(project.Path, path) switch
        {
            SpecsResult.Escaped => Results.BadRequest(new { error = "path escapes the specs root" }),
            SpecsResult.NotFound => Results.NotFound(),
            SpecsResult.Directory d => Results.Ok(new { type = "dir", entries = d.Entries }),
            SpecsResult.FileContent f => Results.Ok(new { type = "file", content = f.Content }),
            _ => Results.Problem("unexpected specs result"),
        };
    }
}
