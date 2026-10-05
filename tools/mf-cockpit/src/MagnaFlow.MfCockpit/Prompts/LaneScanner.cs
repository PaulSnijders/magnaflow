using System.Text.RegularExpressions;

namespace MagnaFlow.MfCockpit.Prompts;

/// <summary>One NNNN-name id as found in docs/prompts/: the parsed cmd file (or the reason it
/// couldn't be parsed) plus whichever pln/qa/rst siblings exist next to it. Summary is the rst's
/// one-line frontmatter summary (docs/decisions/0015-rst-summary-line.md) — null when there is no rst
/// yet, or the rst predates the convention.</summary>
public sealed record LaneItem(
    string Id,
    string FilePath,
    CmdFileLite? Cmd,
    string? Error,
    string? PlnPath,
    string? QaPath,
    string? RstPath,
    string? Summary = null)
{
    public bool IsMalformed => Cmd is null;

    /// <summary>The 4-digit number shared by a command and all its follow-ups (v0.2), e.g. "0005"
    /// for both "0005-fix-lava" and "0005B-round2".</summary>
    public string BaseNumber => Id[..4];

    /// <summary>The follow-up letter (B, C, ...), or null for the original (implicitly "A").</summary>
    public char? Suffix => Id.Length > 4 && Id[4] is >= 'B' and <= 'Z' ? Id[4] : null;

    public bool IsFollowUp => Suffix is not null;
}

/// <summary>
/// Discovers the prompt lane: every NNNN-cmd-name.md directly under docs/prompts/, grouped with
/// its optional pln/qa/rst siblings by the shared NNNN-name id (tools/mf-spec/system.md). Read-only
/// — the cockpit's own writes (draft creation, draft-&gt;ready, follow-up) go through CmdFileLite
/// directly, not through this scanner. Own implementation, not a reference to
/// MagnaFlow.WorkerController.Prompts.PromptScanner (ontwerp-v0.1.md "Tech").
///
/// v0.2 follow-up commands (0005 -&gt; 0005B -&gt; 0005C, ...) widen the id grammar from \d{4} to
/// \d{4}[B-Z]? — the letter is a human-facing convention only, sorting and grouping fall out of
/// plain ordinal string ordering for free (see ontwerp-v0.1.md).
/// </summary>
public static class LaneScanner
{
    private static readonly Regex CmdFileName = new(@"^(?<num>\d{4}[B-Z]?)-cmd-(?<name>[a-z0-9-]+)\.md$", RegexOptions.Compiled);
    // Deliberately requires the hyphen immediately after the 4 digits — a suffixed file
    // ("0005B-...") never matches this, so it's naturally invisible to plain-NNNN allocation
    // (the base number is still accounted for via the plain sibling that must already exist).
    private static readonly Regex AnyPlainNumberedFile = new(@"^(?<num>\d{4})-", RegexOptions.Compiled);
    private static readonly Regex NumberedFileHead = new(@"^(?<num>\d{4})(?<suffix>[B-Z])?-", RegexOptions.Compiled);

    public static string PromptsRoot(string projectRoot) => Path.Combine(projectRoot, "docs", "prompts");

    public static IReadOnlyList<LaneItem> Scan(string projectRoot)
    {
        var root = PromptsRoot(projectRoot);
        if (!Directory.Exists(root))
            return [];

        var files = Directory.EnumerateFiles(root).Select(Path.GetFileName).Where(f => f is not null)
            .Cast<string>().ToList();
        var results = new List<LaneItem>();

        foreach (var fileName in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            var match = CmdFileName.Match(fileName);
            if (!match.Success)
                continue;

            var num = match.Groups["num"].Value;
            var name = match.Groups["name"].Value;
            var id = $"{num}-{name}";
            var filePath = Path.Combine(root, fileName);
            var (cmd, error) = CmdFileLite.Parse(filePath, id);
            var rstPath = SiblingIfExists(root, num, "rst", name);

            results.Add(new LaneItem(
                id, filePath, cmd, error,
                PlnPath: SiblingIfExists(root, num, "pln", name),
                QaPath: SiblingIfExists(root, num, "qa", name),
                RstPath: rstPath,
                Summary: RstSummary.Read(rstPath)));
        }

        return [.. results.OrderBy(r => r.Id, StringComparer.Ordinal)];
    }

    private static string? SiblingIfExists(string root, string num, string kind, string name)
    {
        var path = Path.Combine(root, $"{num}-{kind}-{name}.md");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Next free 4-digit prefix (ontwerp-v0.1.md "Guards": "the id is unique (next free
    /// NNNN)"). Scans every plain NNNN-*.md in the lane, not just well-formed cmd files, so a
    /// malformed or orphaned sibling can never collide with a freshly created draft — and ignores
    /// follow-up-suffixed siblings, which never denote a distinct base number of their own.</summary>
    public static string NextFreeNumber(string projectRoot)
    {
        var root = PromptsRoot(projectRoot);
        if (!Directory.Exists(root))
            return "0001";

        var max = 0;
        foreach (var file in Directory.EnumerateFiles(root))
        {
            var match = AnyPlainNumberedFile.Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups["num"].Value, out var n) && n > max)
                max = n;
        }
        return (max + 1).ToString("D4");
    }

    /// <summary>Next free follow-up letter (B, then C, ...) for a given 4-digit base number,
    /// across every file sharing that number (cmd or sibling, well-formed or not) — same
    /// never-collide posture as NextFreeNumber. Null when B..Z are all taken.</summary>
    public static char? NextFreeSuffix(string projectRoot, string baseNumber)
    {
        var root = PromptsRoot(projectRoot);
        var used = new HashSet<char>();
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.EnumerateFiles(root))
            {
                var match = NumberedFileHead.Match(Path.GetFileName(file));
                if (match.Success && match.Groups["num"].Value == baseNumber && match.Groups["suffix"].Success)
                    used.Add(match.Groups["suffix"].Value[0]);
            }
        }
        for (var c = 'B'; c <= 'Z'; c++)
            if (!used.Contains(c))
                return c;
        return null;
    }

    /// <summary>Slugifies a title into the "name" half of an id: lowercase, non [a-z0-9] runs
    /// collapsed to a single hyphen, leading/trailing hyphens trimmed (must satisfy the same
    /// [a-z0-9-]+ shape the worker controller's own scanner requires).</summary>
    public static string Slugify(string title)
    {
        var lowered = title.Trim().ToLowerInvariant();
        var slug = Regex.Replace(lowered, @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "untitled" : slug;
    }
}
