using System.Text.RegularExpressions;

namespace MagnaFlow.WorkerController.Prompts;

/// <summary>A command as found on disk: either a parsed CmdFile or an error describing why it is malformed.</summary>
public sealed record ScannedCommand(string Id, string FilePath, CmdFile? Cmd, string? Error)
{
    public bool IsMalformed => Cmd is null;
}

/// <summary>
/// Discovers commands: every `NNNN-cmd-name.md` directly under docs/prompts/ (spec FR-001/002).
/// docs/prompts/ is flat (not per-command folders, unlike v0.1's .magnaflow/tasks/); `NNNN-name`
/// is the command's unique ID and ordering key, shared by its optional pln/qa/rst siblings.
/// Malformed commands (and orphaned siblings with no matching cmd file) are reported, never
/// guessed at, and never crash a scan (spec edge case).
/// </summary>
public static class PromptScanner
{
    // \d{4}[B-Z]? — a follow-up command (0005B, 0005C, ...) widens only the numeric prefix; the
    // letter is a human-facing convention (parent implicitly "A"), never itself parsed for meaning
    // beyond "this id sorts and groups next to its base number" (v0.2 follow-up commands).
    public static readonly Regex IdPattern = new(@"^\d{4}[B-Z]?-[a-z0-9-]+$", RegexOptions.Compiled);
    private static readonly Regex CmdFileName = new(@"^(?<num>\d{4}[B-Z]?)-cmd-(?<name>[a-z0-9-]+)\.md$", RegexOptions.Compiled);
    private static readonly Regex SiblingFileName = new(@"^(?<num>\d{4}[B-Z]?)-(?:pln|qa|rst)-(?<name>[a-z0-9-]+)\.md$", RegexOptions.Compiled);

    public static string PromptsRoot(string projectRoot) => Path.Combine(projectRoot, "docs", "prompts");

    public static IReadOnlyList<ScannedCommand> Scan(string projectRoot)
    {
        var root = PromptsRoot(projectRoot);
        if (!Directory.Exists(root))
            return [];

        var results = new List<ScannedCommand>();
        var cmdIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(root).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(file);
            var match = CmdFileName.Match(fileName);
            if (!match.Success)
                continue;

            var id = $"{match.Groups["num"].Value}-{match.Groups["name"].Value}";
            cmdIds.Add(id);
            var (cmd, error) = CmdFile.Parse(file, id);
            results.Add(new ScannedCommand(id, file, cmd, error));
        }

        // Orphaned pln/qa/rst siblings with no matching cmd file are reported, not silently ignored
        // (spec edge case: the cmd file is the anchoring identity for the set).
        foreach (var file in Directory.EnumerateFiles(root))
        {
            var fileName = Path.GetFileName(file);
            var match = SiblingFileName.Match(fileName);
            if (!match.Success)
                continue;

            var id = $"{match.Groups["num"].Value}-{match.Groups["name"].Value}";
            if (!cmdIds.Contains(id))
                results.Add(new ScannedCommand(id, file, null, $"orphaned sibling file '{fileName}' has no matching {match.Groups["num"].Value}-cmd-{match.Groups["name"].Value}.md"));
        }

        return [.. results.OrderBy(r => r.Id, StringComparer.Ordinal)];
    }

    /// <summary>First well-formed command with status ready, in ID order (spec FR-024). Null when nothing is ready.</summary>
    public static ScannedCommand? FirstReady(IReadOnlyList<ScannedCommand> commands) =>
        commands.FirstOrDefault(c => c.Cmd is { Status: CmdStatus.Ready });

    public static ScannedCommand? FindById(IReadOnlyList<ScannedCommand> commands, string id) =>
        commands.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
}
