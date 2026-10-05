using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfCockpit.Prompts;

/// <summary>
/// Read-mostly view of one NNNN-cmd-name.md: YAML frontmatter the cockpit renders
/// (tools/mf-spec/system.md, ontwerp-v0.1.md "What it shows" — status, title, attempts,
/// max_attempts, branch, base, group, specs, created) + the raw body. The cockpit's only two
/// writes ever touch the status: value (draft creation, draft-&gt;ready) — never the body, same
/// surgical-rewrite discipline as MagnaFlow.WorkerController.Prompts.CmdFile, reimplemented here
/// standalone (no library reference, ontwerp-v0.1.md "Tech").
/// </summary>
public sealed class CmdFileLite
{
    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public required CmdStatus Status { get; init; }
    public string? Branch { get; init; }
    public string? Base { get; init; }
    public string? Group { get; init; }
    public string? Created { get; init; }
    public IReadOnlyList<string> Specs { get; init; } = [];
    public int Attempts { get; init; }
    public int? MaxAttempts { get; init; }
    public required string Body { get; init; }

    /// <summary>The numeric prefix, plus an optional follow-up letter (v0.2), e.g. "0007" from
    /// "0007-add-export-button" or "0007B" from "0007B-add-export-button-round2".</summary>
    public string Number => Id[..IdShape.NumberLength(Id)];
    public string Name => Id[(IdShape.NumberLength(Id) + 1)..];

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Parses a cmd file. Returns an error message instead of throwing on malformed input —
    /// a malformed lane file is shown as such, never crashes a scan.</summary>
    public static (CmdFileLite? Cmd, string? Error) Parse(string filePath, string id)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"cannot read file: {ex.Message}");
        }

        var block = FrontmatterBlock.Find(text);
        if (block is null)
            return (null, "missing YAML frontmatter (expected a '---' fenced block at the top)");

        FrontmatterDto dto;
        try
        {
            dto = Deserializer.Deserialize<FrontmatterDto>(block.Value.Yaml) ?? new FrontmatterDto();
        }
        catch (Exception ex)
        {
            return (null, $"invalid YAML frontmatter: {ex.Message}");
        }

        return (new CmdFileLite
        {
            Id = id,
            FilePath = filePath,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? id[(IdShape.NumberLength(id) + 1)..].Replace('-', ' ') : dto.Title.Trim(),
            Status = CmdStatusParser.Parse(dto.Status),
            Branch = string.IsNullOrWhiteSpace(dto.Branch) ? null : dto.Branch.Trim(),
            Base = string.IsNullOrWhiteSpace(dto.Base) ? null : dto.Base.Trim(),
            Group = string.IsNullOrWhiteSpace(dto.Group) ? null : dto.Group.Trim(),
            Created = string.IsNullOrWhiteSpace(dto.Created) ? null : dto.Created.Trim(),
            Specs = dto.Specs ?? [],
            Attempts = dto.Attempts ?? 0,
            MaxAttempts = dto.MaxAttempts,
            Body = text[block.Value.BodyStart..],
        }, null);
    }

    /// <summary>Writes a brand-new draft cmd file (write #1, ontwerp-v0.1.md "Invariant: buttons, not an actor").
    /// The optional branch/base/resume parameters exist for follow-up drafts (write #3, v0.2), which
    /// copy branch/base from the parent and set resume: from the parent's recorded session — a
    /// plain new draft never passes them.</summary>
    public static void WriteDraft(
        string filePath, string title, string body, string createdIso, string? group, IReadOnlyList<string>? specs,
        string? branch = null, string? @base = null, string? resume = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: {title}");
        sb.AppendLine("status: draft");
        sb.AppendLine("attempts: 0");
        if (!string.IsNullOrWhiteSpace(branch))
            sb.AppendLine($"branch: {branch}");
        if (!string.IsNullOrWhiteSpace(@base))
            sb.AppendLine($"base: {@base}");
        if (!string.IsNullOrWhiteSpace(group))
            sb.AppendLine($"group: {group}");
        if (!string.IsNullOrWhiteSpace(resume))
            sb.AppendLine($"resume: {resume}");
        if (specs is { Count: > 0 })
        {
            sb.AppendLine("specs:");
            foreach (var s in specs)
                sb.AppendLine($"  - {s}");
        }
        sb.AppendLine($"created: {createdIso}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(body.Trim());
        sb.AppendLine();
        File.WriteAllText(filePath, sb.ToString());
    }

    /// <summary>Rewrites only the status: value inside the frontmatter block — the same file's
    /// title, body, and every other line survive byte-for-byte.</summary>
    public static void WriteStatus(string filePath, CmdStatus newStatus)
    {
        var bytes = File.ReadAllBytes(filePath);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));

        var block = FrontmatterBlock.Find(text)
            ?? throw new InvalidOperationException($"{filePath}: frontmatter block disappeared");

        // Trailing \r?: with CRLF line endings, multiline $ matches right before \n, not before
        // the \r — without capturing it explicitly here (and re-appending it below) a Windows
        // CRLF file's line ending would silently get eaten by the rewrite.
        var pattern = new Regex(@"^(?<prefix>status:[ \t]*)(?<value>[^#\r\n]*?)(?<suffix>[ \t]*(?:#[^\r\n]*)?)(?<eol>\r?)$", RegexOptions.Multiline);
        var yaml = block.Yaml;
        var match = pattern.Match(yaml);
        if (!match.Success)
            throw new InvalidOperationException($"{filePath}: frontmatter has no status: line to rewrite");

        var newYaml = yaml[..match.Index]
            + match.Groups["prefix"].Value + newStatus.ToYaml() + match.Groups["suffix"].Value + match.Groups["eol"].Value
            + yaml[(match.Index + match.Length)..];

        var newText = text[..block.YamlStart] + newYaml + text[block.YamlEnd..];
        var outBytes = Encoding.UTF8.GetBytes(newText);
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        if (hasBom)
            stream.Write([0xEF, 0xBB, 0xBF]);
        stream.Write(outBytes);
    }

    private sealed class FrontmatterDto
    {
        public string? Title { get; set; }
        public string? Status { get; set; }
        public string? Branch { get; set; }
        public string? Base { get; set; }
        public string? Group { get; set; }
        public bool? FreshSession { get; set; }
        public string? Resume { get; set; }
        public List<string>? Specs { get; set; }
        public int? Attempts { get; set; }
        public int? MaxAttempts { get; set; }
        public string? Created { get; set; }
    }
}

internal static class FrontmatterBlock
{
    private static readonly Regex Fence = new(@"^---[ \t]*\r?$", RegexOptions.Multiline);

    public static (string Yaml, int YamlStart, int YamlEnd, int BodyStart)? Find(string text)
    {
        var open = Fence.Match(text);
        if (!open.Success || text[..open.Index].Trim().Length != 0)
            return null;
        var close = Fence.Match(text, open.Index + open.Length);
        if (!close.Success)
            return null;

        var yamlStart = Math.Min(open.Index + open.Length + 1, text.Length);
        var yamlEnd = close.Index;
        var bodyStart = Math.Min(close.Index + close.Length + 1, text.Length);
        return (text[yamlStart..yamlEnd], yamlStart, yamlEnd, bodyStart);
    }
}
