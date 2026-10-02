using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Live;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Projects;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>
/// Write #6 (ontwerp-v0.4.md "Add project"): GET /api/new-project feeds the dialog (template names
/// only — definitions never leave the server); POST /api/projects scaffolds-or-registers a project,
/// "mode: new" or "mode: existing". One process-wide lock across scaffold + register so two
/// simultaneous adds cannot interleave on magnaflow.yml (ontwerp-v0.4.md "Guards").
/// </summary>
public static class NewProjectEndpoints
{
    // Shared with the remove-project (write #7) endpoint: every magnaflow.yml text edit (append or
    // remove) serializes on this one process-wide gate so two config writes cannot interleave.
    internal static readonly SemaphoreSlim ConfigWriteGate = new(1, 1);

    public static void MapNewProjectApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/new-project", (NewProjectConfig newProjectConfig) =>
            Results.Ok(new NewProjectInfoDto(
                newProjectConfig.Root,
                newProjectConfig.Templates.Select(t => t.Name).ToList(),
                !string.IsNullOrWhiteSpace(newProjectConfig.SpecKit))));

        app.MapPost("/api/projects", (CreateProjectRequest request, CockpitConfig config, NewProjectConfig newProjectConfig,
                ProjectRegistry registry, ProjectScaffolder scaffolder, SseHub hub) =>
            string.Equals(request.Mode, "existing", StringComparison.OrdinalIgnoreCase)
                ? HandleExistingAsync(request, config, newProjectConfig, registry, hub)
                : string.Equals(request.Mode, "new", StringComparison.OrdinalIgnoreCase)
                    ? HandleNewAsync(request, config, registry, scaffolder, hub)
                    : Task.FromResult(Results.BadRequest(new { error = "mode must be 'new' or 'existing'" })));
    }

    private static async Task<IResult> HandleNewAsync(
        CreateProjectRequest request, CockpitConfig config, ProjectRegistry registry, ProjectScaffolder scaffolder, SseHub hub)
    {
        await ConfigWriteGate.WaitAsync();
        try
        {
            var validation = scaffolder.Validate(request.Name, request.Template, registry);
            if (!validation.IsOk)
                return ValidationError(validation);

            var name = request.Name.Trim();
            var scaffold = await scaffolder.ScaffoldAsync(name, validation.DirName!, validation.TargetPath!, request.Template);
            if (!scaffold.Success)
            {
                return Results.UnprocessableEntity(new
                {
                    error = scaffold.Error,
                    output = scaffold.TemplateOutput,
                    path = scaffold.TargetPath,
                    warnings = scaffold.Warnings,
                });
            }

            var entry = new ProjectEntry { Name = name, Path = scaffold.TargetPath };
            var appended = await MagnaflowYmlAppender.AppendProjectAsync(config, entry);
            if (appended.Outcome != MagnaflowYmlAppender.Outcome.Ok)
                return Results.Problem(appended.Error, statusCode: StatusCodes.Status500InternalServerError);

            // Registration is deliberately last (ontwerp-v0.4.md): a failed scaffold or a failed
            // config append never leaves a phantom in-memory entry.
            registry.Add(entry);
            hub.Broadcast(new LiveEvent(name, "projects"));

            return Results.Ok(new CreateProjectResponse(name, scaffold.TargetPath, scaffold.Warnings, scaffold.SeededDraft?.Id));
        }
        finally
        {
            ConfigWriteGate.Release();
        }
    }

    private static async Task<IResult> HandleExistingAsync(
        CreateProjectRequest request, CockpitConfig config, NewProjectConfig newProjectConfig, ProjectRegistry registry, SseHub hub)
    {
        await ConfigWriteGate.WaitAsync();
        try
        {
            var name = (request.Name ?? "").Trim();
            if (name.Length == 0)
                return Results.BadRequest(new { error = "name is required" });
            if (string.IsNullOrWhiteSpace(request.Path))
                return Results.BadRequest(new { error = "path is required" });

            var resolvedPath = ResolvePath(request.Path, newProjectConfig.Root);
            if (resolvedPath is null)
            {
                return Results.BadRequest(new
                {
                    error = "path must be absolute — cockpit.new_project.root is not configured to resolve a relative path against",
                });
            }

            if (!Directory.Exists(resolvedPath))
                return Results.BadRequest(new { error = $"'{resolvedPath}' does not exist or is not a directory" });

            if (registry.ExistsByName(name) || registry.ExistsByPath(resolvedPath))
                return Results.Conflict(new { error = $"'{name}' or '{resolvedPath}' is already registered" });

            var warnings = new List<string>();
            if (!Directory.Exists(Path.Combine(resolvedPath, ".git")))
                warnings.Add("no .git directory found at this path — git features will show as unavailable");

            var entry = new ProjectEntry { Name = name, Path = resolvedPath };
            var appended = await MagnaflowYmlAppender.AppendProjectAsync(config, entry);
            if (appended.Outcome != MagnaflowYmlAppender.Outcome.Ok)
                return Results.Problem(appended.Error, statusCode: StatusCodes.Status500InternalServerError);

            registry.Add(entry);
            hub.Broadcast(new LiveEvent(name, "projects"));

            return Results.Ok(new CreateProjectResponse(name, resolvedPath, warnings, null));
        }
        finally
        {
            ConfigWriteGate.Release();
        }
    }

    private static string? ResolvePath(string path, string? root)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(Path.Combine(root, path));
    }

    private static IResult ValidationError(ProjectScaffolder.ValidationResult v) => v.Outcome switch
    {
        ProjectScaffolder.ValidationOutcome.NameNotUnique => Results.Conflict(new { error = v.Error }),
        ProjectScaffolder.ValidationOutcome.TargetExists => Results.Conflict(new { error = v.Error }),
        _ => Results.BadRequest(new { error = v.Error }),
    };
}
