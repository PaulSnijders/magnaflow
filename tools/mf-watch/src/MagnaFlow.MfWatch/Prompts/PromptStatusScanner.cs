using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfWatch.Prompts;

/// <summary>One NNNN-cmd-name.md as mf-watch needs it: id, status, and a display title — nothing
/// else. mf-watch "knows frontmatter statuses and exit codes, nothing of internal formats"
/// (ontwerp-v0.1.md's invariant), so this deliberately does not reuse or reimplement the worker
/// controller's full CmdFile (branch/base/group/resume/specs/attempts) — those fields are the
/// worker's business, not the dispatcher's.</summary>
public sealed record ScannedCommand(string Id, string FilePath, string? Status, string? Title)
{
    /// <summary>Status values are opaque strings to mf-watch (lowercase, trimmed); it only ever
    /// compares against the literals it cares about, never enumerates the full set.</summary>
    public bool IsReady => Status == "ready";
    public bool IsRunning => Status == "running";
}

/// <summary>
/// Discovers commands under docs/prompts/ the same way MagnaFlow.WorkerController.Prompts.
/// PromptScanner does (flat NNNN-cmd-name.md files), but reads only frontmatter, never the body.
/// </summary>
public static class PromptStatusScanner
{
    // \d{4}[B-Z]? — a follow-up command (0005B, 0005C, ...); mf-watch treats it exactly like any
    // other id (a status and an exit code, nothing else), so widening the grammar is the only
    // change needed here (v0.2 follow-up commands, mf-cockpit's ontwerp-v0.1.md).
    private static readonly Regex CmdFileName = new(@"^(?<num>\d{4}[B-Z]?)-cmd-(?<name>[a-z0-9-]+)\.md$", RegexOptions.Compiled);
    private static readonly Regex Fence = new(@"^---[ \t]*\r?$", RegexOptions.Multiline);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string PromptsRoot(string projectRoot) => Path.Combine(projectRoot, "docs", "prompts");

    public static IReadOnlyList<ScannedCommand> Scan(string projectRoot, Action<string>? onWarning = null)
    {
        var root = PromptsRoot(projectRoot);
        if (!Directory.Exists(root))
            return [];

        var results = new List<ScannedCommand>();
        foreach (var file in Directory.EnumerateFiles(root).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            var match = CmdFileName.Match(Path.GetFileName(file));
            if (!match.Success)
                continue;

            var id = $"{match.Groups["num"].Value}-{match.Groups["name"].Value}";
            var (status, title, error) = ReadFrontmatter(file);
            if (error is not null)
            {
                onWarning?.Invoke($"[{id}] {error}");
                continue;
            }
            results.Add(new ScannedCommand(id, file, status, title));
        }

        return results;
    }

    /// <summary>Re-reads just the status of one command file (after spawning the worker on it).</summary>
    public static string? ReadStatus(string filePath) => ReadFrontmatter(filePath).Status;

    private static (string? Status, string? Title, string? Error) ReadFrontmatter(string filePath)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null, $"cannot read file: {ex.Message}");
        }

        var open = Fence.Match(text);
        if (!open.Success || text[..open.Index].Trim().Length != 0)
            return (null, null, "missing YAML frontmatter (expected a '---' fenced block at the top)");
        var close = Fence.Match(text, open.Index + open.Length);
        if (!close.Success)
            return (null, null, "missing closing '---' for YAML frontmatter");

        var yamlStart = Math.Min(open.Index + open.Length + 1, text.Length);
        var yaml = text[yamlStart..close.Index];

        FrontmatterDto dto;
        try
        {
            dto = Deserializer.Deserialize<FrontmatterDto>(yaml) ?? new FrontmatterDto();
        }
        catch (Exception ex)
        {
            return (null, null, $"invalid YAML frontmatter: {ex.Message}");
        }

        var status = string.IsNullOrWhiteSpace(dto.Status) ? null : dto.Status.Trim().ToLowerInvariant();
        var title = string.IsNullOrWhiteSpace(dto.Title) ? null : dto.Title.Trim();
        return (status, title, null);
    }

    private sealed class FrontmatterDto
    {
        public string? Title { get; set; }
        public string? Status { get; set; }
    }
}
