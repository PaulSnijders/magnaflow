using MagnaFlow.MfCockpit.Evidence;
using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Prompts;

/// <summary>
/// The cockpit's only three writes (ontwerp-v0.1.md "Invariant: buttons, not an actor"): create a
/// draft cmd file, flip exactly draft-&gt;ready, and (v0.2) create a follow-up draft from a
/// terminal command. All three are followed immediately by a git commit of that one file — never
/// a batch, never anything else in the lane.
/// </summary>
public sealed class DraftWriter(IGitClient git, IClock clock)
{
    public sealed record CreateResult(string Id, string FilePath);

    public enum ReadyOutcome { Ok, NotFound, WrongStatus }

    public enum FollowUpOutcome { Ok, ParentNotFound, SuffixesExhausted }

    public sealed record FollowUpResult(string Id, string FilePath, bool ResumeSet, string? Warning);

    public async Task<CreateResult> CreateDraftAsync(
        string projectRoot, string title, string body, string? group, IReadOnlyList<string>? specs)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("title is required");

        var number = LaneScanner.NextFreeNumber(projectRoot);
        var name = LaneScanner.Slugify(title);
        var id = $"{number}-{name}";
        var path = Path.Combine(LaneScanner.PromptsRoot(projectRoot), $"{number}-cmd-{name}.md");
        var createdIso = clock.UtcNow.ToString("yyyy-MM-dd");

        CmdFileLite.WriteDraft(path, title.Trim(), body ?? "", createdIso, group, specs);

        var relative = Path.GetRelativePath(projectRoot, path);
        await git.CommitFileAsync(projectRoot, relative, $"cockpit: create draft {id}");

        return new CreateResult(id, path);
    }

    /// <summary>The human's "go" button: exactly draft-&gt;ready, 409 on any other current status
    /// (ontwerp-v0.1.md "Guards": "the status transition is exactly draft→ready; anything else is
    /// a 409").</summary>
    public async Task<ReadyOutcome> FlipToReadyAsync(string projectRoot, string id)
    {
        var item = LaneScanner.Scan(projectRoot).FirstOrDefault(i => i.Id == id);
        if (item?.Cmd is null)
            return ReadyOutcome.NotFound;
        if (item.Cmd.Status != CmdStatus.Draft)
            return ReadyOutcome.WrongStatus;

        CmdFileLite.WriteStatus(item.FilePath, CmdStatus.Ready);

        var relative = Path.GetRelativePath(projectRoot, item.FilePath);
        await git.CommitFileAsync(projectRoot, relative, $"cockpit: ready {id}");

        return ReadyOutcome.Ok;
    }

    /// <summary>
    /// Write #3 (v0.2): a short "good, but..." round on a terminal command, without a cold
    /// context. Naming is 0005 -&gt; 0005B -&gt; 0005C, ... (ontwerp-v0.1.md) — the letter is a
    /// human-facing convention only; the worker controller's own scanner grammar is what actually
    /// accepts it. Copies branch/base/group/specs from the parent, and sets resume: to the
    /// parent's own recorded session id (a raw agent session id — MagnaFlow.WorkerController.
    /// Execution.ResumeResolver's "anything else" path, verified against that source rather than
    /// invented) so the worker continues the exact same Claude session. A parent with no recorded
    /// session still gets a draft — just without resume:, and a warning the caller can surface.
    /// </summary>
    public async Task<(FollowUpOutcome Outcome, FollowUpResult? Result)> CreateFollowUpAsync(
        string projectRoot, string parentId, string feedback, string? slug)
    {
        if (string.IsNullOrWhiteSpace(feedback))
            throw new ArgumentException("feedback is required");

        var items = LaneScanner.Scan(projectRoot);
        var parent = items.FirstOrDefault(i => i.Id == parentId);
        if (parent?.Cmd is null)
            return (FollowUpOutcome.ParentNotFound, null);

        var suffix = LaneScanner.NextFreeSuffix(projectRoot, parent.BaseNumber);
        if (suffix is null)
            return (FollowUpOutcome.SuffixesExhausted, null);

        var name = string.IsNullOrWhiteSpace(slug) ? parent.Cmd.Name : LaneScanner.Slugify(slug);
        var number = $"{parent.BaseNumber}{suffix}";
        var id = $"{number}-{name}";
        var path = Path.Combine(LaneScanner.PromptsRoot(projectRoot), $"{number}-cmd-{name}.md");

        var sessionId = EvidenceReader.ReadSessionId(projectRoot, parentId);
        var warning = sessionId is null ? "no recorded session — will start cold" : null;

        var title = $"{parent.Cmd.Title} (follow-up)";
        var body = BuildFollowUpBody(feedback, parent);
        var createdIso = clock.UtcNow.ToString("yyyy-MM-dd");

        CmdFileLite.WriteDraft(
            path, title, body, createdIso, parent.Cmd.Group, parent.Cmd.Specs,
            branch: parent.Cmd.Branch, @base: parent.Cmd.Base, resume: sessionId);

        var relative = Path.GetRelativePath(projectRoot, path);
        await git.CommitFileAsync(projectRoot, relative, $"cockpit: create follow-up draft {id} (from {parentId})");

        return (FollowUpOutcome.Ok, new FollowUpResult(id, path, sessionId is not null, warning));
    }

    private static string BuildFollowUpBody(string feedback, LaneItem parent)
    {
        var parentFileName = Path.GetFileName(parent.FilePath);
        var reference = parent.RstPath is { } rstPath
            ? $"Follow-up to [{parent.Id}](./{parentFileName}) — see [its report](./{Path.GetFileName(rstPath)})."
            : $"Follow-up to [{parent.Id}](./{parentFileName}).";

        return $"{feedback.Trim()}\n\n---\n{reference}\n";
    }
}
