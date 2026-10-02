using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>The per-project scratchpad (ontwerp-v0.5.md item 8): a plain-text notepad kept outside
/// git, next to the loaded magnaflow.yml. The endpoint takes no path parameter at all — the filename
/// is derived from the project name server-side (ScratchpadStore), so there is nothing to escape.
/// GET hands out content + a hash; PUT enforces hash match (409) then the 256 KB cap (400), mirroring
/// the config PUT.</summary>
public static class ScratchpadEndpoints
{
    public static void MapScratchpadApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{name}/scratchpad", (string name, ProjectRegistry registry, ScratchpadStore store) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            var snapshot = store.Read(project.Name);
            return Results.Ok(new ScratchpadDto(snapshot.Content, snapshot.Hash, snapshot.SavedAt));
        });

        app.MapPut("/api/projects/{name}/scratchpad", async (string name, ScratchpadSaveRequest request, ProjectRegistry registry, ScratchpadStore store) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            // Off the request thread pool's synchronous file I/O — the store is sync (like
            // ProjectConfigReader), so hop once rather than block a request thread on disk.
            var result = await Task.Run(() => store.Save(project.Name, request.Content ?? "", request.BaseHash ?? ""));
            return result.Outcome switch
            {
                ScratchpadStore.SaveOutcome.Conflict =>
                    Results.Conflict(new { error = "a newer version is on the server — reload, or keep yours to overwrite" }),
                ScratchpadStore.SaveOutcome.TooLarge =>
                    Results.BadRequest(new { error = $"scratchpad exceeds the {ScratchpadStore.MaxContentBytes / 1024} KB size cap" }),
                _ => Results.Ok(new ScratchpadSaveResponse(result.Hash!, result.SavedAt!)),
            };
        });
    }
}
