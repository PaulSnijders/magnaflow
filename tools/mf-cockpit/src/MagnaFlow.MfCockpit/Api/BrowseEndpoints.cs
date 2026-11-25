using MagnaFlow.MfCockpit.Projects;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>
/// GET /api/browse (docs/specs/cockpit/index.md, "Browse"): the Add project → Existing folder
/// picker. Without `path` it lists the start folder (from `hint`, the dialog's Path field). Writes
/// nothing; only directory names leave the server.
/// </summary>
public static class BrowseEndpoints
{
    public static void MapBrowseApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/browse", (HttpRequest request, DirectoryBrowser browser) =>
        {
            // Present-but-empty `path=` is a 400 (spec), so test presence, not value.
            var path = request.Query.ContainsKey("path")
                ? request.Query["path"].ToString()
                : browser.StartPath(request.Query["hint"].ToString());

            var result = browser.List(path);
            return result.Error switch
            {
                DirectoryBrowser.ErrorKind.None => Results.Ok(result.Listing),
                DirectoryBrowser.ErrorKind.NotFound => Results.NotFound(new { error = result.Message }),
                DirectoryBrowser.ErrorKind.Denied => Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status403Forbidden),
                _ => Results.BadRequest(new { error = result.Message }),
            };
        });
    }
}
