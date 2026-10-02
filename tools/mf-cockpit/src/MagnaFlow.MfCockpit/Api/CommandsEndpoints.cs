using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Evidence;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Api;

/// <summary>The lane table, a command's full drill-down, and the cockpit's only two writes
/// (ontwerp-v0.1.md "API" + "Invariant: buttons, not an actor").</summary>
public static class CommandsEndpoints
{
    public static void MapCommandsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{name}/commands", (string name, ProjectRegistry registry, IClock clock) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            var items = LaneScanner.Scan(project.Path);
            return Results.Ok(items.Select(i => ToSummary(project.Path, i, clock)).ToList());
        });

        app.MapGet("/api/projects/{name}/commands/{id}", (string name, string id, ProjectRegistry registry) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();
            var item = LaneScanner.Scan(project.Path).FirstOrDefault(i => i.Id == id);
            if (item is null)
                return Results.NotFound();
            return Results.Ok(ToDetail(project.Path, item));
        });

        app.MapPost("/api/projects/{name}/commands", async (string name, CreateDraftRequest request, ProjectRegistry registry, DraftWriter writer) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            try
            {
                var result = await writer.CreateDraftAsync(project.Path, request.Title, request.Body ?? "", request.Group, request.Specs);
                return Results.Ok(new CreateDraftResponse(result.Id, result.FilePath));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (GitException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/api/projects/{name}/commands/{id}/ready", async (string name, string id, ProjectRegistry registry, DraftWriter writer) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            try
            {
                var outcome = await writer.FlipToReadyAsync(project.Path, id);
                return outcome switch
                {
                    DraftWriter.ReadyOutcome.Ok => Results.Ok(new { id, status = "ready" }),
                    DraftWriter.ReadyOutcome.NotFound => Results.NotFound(),
                    DraftWriter.ReadyOutcome.WrongStatus => Results.Conflict(new { error = $"'{id}' is not in draft status" }),
                    _ => Results.Problem("unexpected outcome"),
                };
            }
            catch (GitException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/api/projects/{name}/commands/{id}/follow-up", async (string name, string id, FollowUpRequest request, ProjectRegistry registry, DraftWriter writer) =>
        {
            var project = ProjectResolver.Find(registry, name);
            if (project is null)
                return Results.NotFound();

            try
            {
                var (outcome, result) = await writer.CreateFollowUpAsync(project.Path, id, request.Feedback, request.Slug);
                return outcome switch
                {
                    DraftWriter.FollowUpOutcome.Ok => Results.Ok(new FollowUpResponse(result!.Id, result.FilePath, result.ResumeSet, result.Warning)),
                    DraftWriter.FollowUpOutcome.ParentNotFound => Results.Conflict(new { error = $"'{id}' is not a known command — cannot create a follow-up" }),
                    DraftWriter.FollowUpOutcome.SuffixesExhausted => Results.Conflict(new { error = $"no free follow-up letter left for '{id}' (B..Z all taken)" }),
                    _ => Results.Problem("unexpected outcome"),
                };
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (GitException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }

    private static CommandSummaryDto ToSummary(string projectRoot, LaneItem item, IClock clock) => new(
        item.Id,
        item.Cmd?.Title ?? item.Id,
        item.Cmd?.Status.ToYaml() ?? "malformed",
        item.Cmd?.Attempts ?? 0,
        item.Cmd?.MaxAttempts,
        item.Cmd?.Branch,
        item.Cmd?.Group,
        item.PlnPath is not null,
        item.QaPath is not null,
        item.RstPath is not null,
        item.IsMalformed,
        item.Error,
        item.BaseNumber,
        item.IsFollowUp,
        item.Summary,
        EvidenceReader.DurationSeconds(projectRoot, item.Id, item.Cmd?.Status == CmdStatus.Running, clock));

    private static CommandDetailDto ToDetail(string projectRoot, LaneItem item)
    {
        var evidence = EvidenceReader.Read(projectRoot, item.Id);
        return new CommandDetailDto(
            item.Id,
            item.Cmd?.Title ?? item.Id,
            item.Cmd?.Status.ToYaml() ?? "malformed",
            item.Cmd?.Attempts ?? 0,
            item.Cmd?.MaxAttempts,
            item.Cmd?.Branch,
            item.Cmd?.Base,
            item.Cmd?.Group,
            item.Cmd?.Specs ?? [],
            item.Cmd?.Created,
            item.IsMalformed,
            item.Error,
            CmdBody(item),
            LaneFile(item.PlnPath),
            LaneFile(item.QaPath),
            LaneFile(item.RstPath),
            ToLogTail(evidence.ClaudeLog),
            ToLogTail(evidence.BuildLog),
            ToLogTail(evidence.TestLog),
            evidence.SessionId,
            item.Summary);
    }

    // The cmd file's frontmatter is already surfaced as structured fields (title/status/attempts/
    // branch/...) above; rendering it again as literal markdown text would just show a "---"
    // block. Show the body only — falling back to the raw file when parsing failed, so a
    // malformed cmd is still fully visible for diagnosis.
    private static LaneFileDto CmdBody(LaneItem item) =>
        item.Cmd is { } cmd ? new LaneFileDto(true, cmd.Body) : LaneFile(item.FilePath);

    private static LaneFileDto LaneFile(string? path) =>
        path is not null && File.Exists(path) ? new LaneFileDto(true, File.ReadAllText(path)) : new LaneFileDto(false, null);

    private static LogTailDto ToLogTail(TailResult r) => new(r.Lines, r.Truncated, r.Exists);
}
