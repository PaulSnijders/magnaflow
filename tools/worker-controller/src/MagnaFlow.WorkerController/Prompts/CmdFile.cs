using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.WorkerController.Prompts;

/// <summary>
/// One parsed NNNN-cmd-name.md: YAML frontmatter (machine metadata) + Markdown body (agent
/// instructions). Writes are surgical: only the status:/attempts: values are ever rewritten, in
/// place, so user comments, formatting, and the body stay byte-for-byte identical (v0.1 research
/// R4, unchanged). The body is never written by the agent (spec FR-005) — only the human authors
/// it; the controller only ever touches status:/attempts:.
/// </summary>
public sealed class CmdFile
{
    /// <summary>The "NNNN-name" identity shared by this command's cmd/pln/qa/rst siblings (spec FR-001).</summary>
    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public required CmdStatus Status { get; init; }
    /// <summary>Work branch. Null = branchless mode: the work happens directly on the invoking branch.</summary>
    public string? Branch { get; init; }
    public string? Base { get; init; }
    public string? Group { get; init; }
    public bool FreshSession { get; init; }
    /// <summary>Explicit session continuation: a raw agent session ID, or a command ID whose recorded session to continue.</summary>
    public string? Resume { get; init; }
    public IReadOnlyList<string> Specs { get; init; } = [];
    public int Attempts { get; init; }
    public int? MaxAttempts { get; init; }
    public required string Body { get; init; }

    /// <summary>The numeric prefix, plus an optional follow-up letter, e.g. "0007" from
    /// "0007-add-export-button" or "0007B" from "0007B-add-export-button-round2".</summary>
    public string Number => Id[..IdShape.NumberLength(Id)];
    /// <summary>The name slug, e.g. "add-export-button" from id "0007-add-export-button".</summary>
    public string Name => Id[(IdShape.NumberLength(Id) + 1)..];
    private string PromptsDirectory => Path.GetDirectoryName(FilePath)!;

    public string PlnPath => Path.Combine(PromptsDirectory, $"{Number}-pln-{Name}.md");
    public string QaPath => Path.Combine(PromptsDirectory, $"{Number}-qa-{Name}.md");
    public string RstPath => Path.Combine(PromptsDirectory, $"{Number}-rst-{Name}.md");

    /// <summary>Machine runtime evidence directory for this command (spec FR-004): logs + session.yml.</summary>
    public static string EvidenceDirectory(string projectRoot, string id) =>
        Path.Combine(projectRoot, ".magnaflow", id);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Parses a cmd file. Returns an error message instead of throwing on malformed input.</summary>
    public static (CmdFile? Cmd, string? Error) Parse(string filePath, string id)
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

        var block = FindFrontmatterBlock(text);
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

        if (!CmdStatusParser.TryParse(dto.Status, out var status))
            return (null, $"frontmatter has missing or unknown 'status' value '{dto.Status}'");
        if (string.IsNullOrWhiteSpace(dto.Branch) && !string.IsNullOrWhiteSpace(dto.Base))
            return (null, "frontmatter has 'base' but no 'branch' — a base only makes sense for a work branch");
        if (!string.IsNullOrWhiteSpace(dto.Resume) && dto.FreshSession == true)
            return (null, "frontmatter has both 'resume' and 'fresh_session: true' — these contradict each other");

        return (new CmdFile
        {
            Id = id,
            FilePath = filePath,
            // Title is optional; fall back to the name slug so status/commits/prompts stay readable.
            Title = string.IsNullOrWhiteSpace(dto.Title) ? id[(IdShape.NumberLength(id) + 1)..].Replace('-', ' ') : dto.Title.Trim(),
            Status = status,
            Branch = string.IsNullOrWhiteSpace(dto.Branch) ? null : dto.Branch.Trim(),
            Base = string.IsNullOrWhiteSpace(dto.Base) ? null : dto.Base.Trim(),
            Group = string.IsNullOrWhiteSpace(dto.Group) ? null : dto.Group.Trim(),
            FreshSession = dto.FreshSession ?? false,
            Resume = string.IsNullOrWhiteSpace(dto.Resume) ? null : dto.Resume.Trim(),
            Specs = dto.Specs ?? [],
            Attempts = dto.Attempts ?? 0,
            MaxAttempts = dto.MaxAttempts,
            Body = text[block.Value.BodyStart..],
        }, null);
    }

    /// <summary>Rewrites only the status: value inside the frontmatter block.</summary>
    public void WriteStatus(CmdStatus newStatus) => UpdateFrontmatterValue(FilePath, "status", newStatus.ToYaml());

    /// <summary>Rewrites only the attempts: value inside the frontmatter block.</summary>
    public void WriteAttempts(int attempts) => UpdateFrontmatterValue(FilePath, "attempts", attempts.ToString());

    internal static void UpdateFrontmatterValue(string filePath, string key, string newValue)
    {
        var bytes = File.ReadAllBytes(filePath);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));

        var block = FindFrontmatterBlock(text)
            ?? throw new InvalidOperationException($"{filePath}: frontmatter block disappeared");

        // Replace only the value token on the key's line; trailing comments survive.
        var pattern = new Regex($@"^(?<prefix>{Regex.Escape(key)}:[ \t]*)(?<value>[^#\r\n]*?)(?<suffix>[ \t]*(?:#[^\r\n]*)?)$",
            RegexOptions.Multiline);
        var yaml = block.Yaml;
        var match = pattern.Match(yaml);

        string newYaml;
        if (match.Success)
        {
            newYaml = yaml[..match.Index]
                + match.Groups["prefix"].Value + newValue + match.Groups["suffix"].Value
                + yaml[(match.Index + match.Length)..];
        }
        else
        {
            // Key absent (e.g. attempts omitted): append as the last frontmatter line.
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            newYaml = yaml.TrimEnd('\r', '\n') + newline + $"{key}: {newValue}" + newline;
        }

        var newText = text[..block.YamlStart] + newYaml + text[block.YamlEnd..];
        var outBytes = Encoding.UTF8.GetBytes(newText);
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        if (hasBom)
            stream.Write([0xEF, 0xBB, 0xBF]);
        stream.Write(outBytes);
    }

    private static (string Yaml, int YamlStart, int YamlEnd, int BodyStart)? FindFrontmatterBlock(string text)
    {
        var fence = new Regex(@"^---[ \t]*\r?$", RegexOptions.Multiline);
        var open = fence.Match(text);
        if (!open.Success || text[..open.Index].Trim().Length != 0)
            return null;
        var close = fence.Match(text, open.Index + open.Length);
        if (!close.Success)
            return null;

        var yamlStart = open.Index + open.Length + 1; // past the newline after the opening fence
        if (yamlStart > text.Length) yamlStart = text.Length;
        var yamlEnd = close.Index;
        var bodyStart = close.Index + close.Length + 1;
        if (bodyStart > text.Length) bodyStart = text.Length;
        return (text[yamlStart..yamlEnd], yamlStart, yamlEnd, bodyStart);
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
