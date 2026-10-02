using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Prompts;

public sealed record CommandCounts(int Draft, int Ready, int Running, int Questions, int Done, int Aborted, int Malformed)
{
    public static CommandCounts From(IReadOnlyList<LaneItem> items)
    {
        int Count(CmdStatus s) => items.Count(i => i.Cmd?.Status == s);
        return new CommandCounts(
            Count(CmdStatus.Draft), Count(CmdStatus.Ready), Count(CmdStatus.Running),
            Count(CmdStatus.Questions), Count(CmdStatus.Done), Count(CmdStatus.Aborted),
            items.Count(i => i.IsMalformed));
    }
}

public sealed record AttentionItem(string Id, string Title, string Status, string Reason);

/// <summary>
/// "Who is at bat?" (ontwerp-v0.1.md index.html: "an attention list — anything questions,
/// aborted, or stale running"). Recomputed fresh on every request rather than tracked over time —
/// the cockpit has no daemon memory, unlike mf-watch's own in-process stale-`running` tracking.
/// </summary>
public static class AttentionRules
{
    public static readonly TimeSpan StaleRunningThreshold = TimeSpan.FromMinutes(15);

    public static IReadOnlyList<AttentionItem> Build(string projectRoot, IReadOnlyList<LaneItem> items, IClock clock)
    {
        // The highest lane id present (ordinal, so 0005 < 0005B < 0006): an `aborted` command
        // stops demanding attention once anything with a higher id exists — a follow-up, or simply
        // the next number, is the human saying "seen it" (ontwerp-v0.5.md item 5). Every lane file
        // counts here, malformed or not: creating a numbered file at all is "moving on".
        var highestId = items.Count == 0
            ? null
            : items.Select(i => i.Id).Aggregate((a, b) => string.CompareOrdinal(a, b) >= 0 ? a : b);

        var result = new List<AttentionItem>();
        foreach (var item in items)
        {
            if (item.Cmd is not { } cmd)
                continue;

            switch (cmd.Status)
            {
                case CmdStatus.Questions:
                    result.Add(new AttentionItem(item.Id, cmd.Title, "questions", "waiting for a human answer"));
                    break;
                case CmdStatus.Aborted when string.CompareOrdinal(item.Id, highestId) >= 0:
                    result.Add(new AttentionItem(item.Id, cmd.Title, "aborted", "run aborted"));
                    break;
                case CmdStatus.Running when IsStale(projectRoot, item, clock):
                    result.Add(new AttentionItem(item.Id, cmd.Title, "running", "no activity for a while — may be stuck"));
                    break;
            }
        }
        return result;
    }

    private static bool IsStale(string projectRoot, LaneItem item, IClock clock)
    {
        var last = Evidence.EvidenceReader.LastActivityUtc(projectRoot, item.Id) ?? SafeLastWrite(item.FilePath);
        return last is null || clock.UtcNow - last.Value > StaleRunningThreshold;
    }

    private static DateTimeOffset? SafeLastWrite(string path) =>
        File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
}
