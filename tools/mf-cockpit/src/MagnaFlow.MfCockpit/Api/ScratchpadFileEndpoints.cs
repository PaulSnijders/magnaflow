using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>The scratchpad file drop (docs/specs/cockpit/scratchpad.md#files): list, raw-body upload,
/// download as attachment, delete. The file name is the one path parameter the scratchpad takes; the
/// store sanitizes it and resolves it inside the project's files directory. Uploads are a raw PUT
/// body (no multipart, so no antiforgery), with this endpoint's body limit raised above Kestrel's
/// 30 MB default to just over the 200 MB cap.</summary>
public static class ScratchpadFileEndpoints
{
    /// <summary>Slightly above the cap so the store, not Kestrel, normally decides "too large".</summary>
    public const long RequestBodyLimit = ScratchpadFileStore.MaxFileBytes + 1024 * 1024;

    private static IResult TooLarge() =>
        Results.Json(new { error = $"file exceeds the {ScratchpadFileStore.MaxFileBytes / (1024 * 1024)} MB cap" },
            statusCode: StatusCodes.Status413PayloadTooLarge);

    private static ScratchpadFileDto ToDto(ScratchpadFileStore.Entry e) => new(e.Name, e.Size, e.UploadedAt);

    public static void MapScratchpadFileApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{name}/files", (string name, ProjectRegistry registry, ScratchpadFileStore store) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            return Results.Ok(store.List(project.Name).Select(ToDto).ToList());
        });

        app.MapPut("/api/projects/{name}/files/{file}", async (string name, string file, HttpContext ctx, ProjectRegistry registry, ScratchpadFileStore store) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            var sizeFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
                sizeFeature.MaxRequestBodySize = RequestBodyLimit;

            if (ctx.Request.ContentLength > ScratchpadFileStore.MaxFileBytes)
                return TooLarge();
            if (ScratchpadFileStore.SanitizeFileName(file) is null)
                return Results.BadRequest(new { error = "invalid file name" });

            try
            {
                var result = await store.SaveAsync(project.Name, file, ctx.Request.Body, ctx.RequestAborted);
                return result.Outcome switch
                {
                    ScratchpadFileStore.SaveOutcome.BadName => Results.BadRequest(new { error = "invalid file name" }),
                    ScratchpadFileStore.SaveOutcome.TooLarge => TooLarge(),
                    _ => Results.Ok(ToDto(result.Entry!)),
                };
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return TooLarge();
            }
        }).WithMetadata(new RequestSizeLimitAttribute(RequestBodyLimit));

        app.MapGet("/api/projects/{name}/files/{file}", (string name, string file, ProjectRegistry registry, ScratchpadFileStore store) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            var stream = store.OpenRead(project.Name, file);
            return stream is null
                ? Results.NotFound()
                : Results.File(stream, "application/octet-stream", fileDownloadName: file);
        });

        app.MapDelete("/api/projects/{name}/files/{file}", (string name, string file, ProjectRegistry registry, ScratchpadFileStore store) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            return store.Delete(project.Name, file) ? Results.NoContent() : Results.NotFound();
        });
    }
}
