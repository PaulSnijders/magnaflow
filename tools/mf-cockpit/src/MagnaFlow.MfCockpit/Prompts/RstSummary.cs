using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfCockpit.Prompts;

/// <summary>
/// Reads the one-line `summary:` out of an NNNN-rst-name.md's frontmatter — nothing else
/// (docs/mf-spec/ontwerp-summary-line.md). Deliberately frontmatter-only: the report body is
/// written for the next AI session and can be arbitrarily long, while this runs for every finished
/// command in a project's lane on every refresh. Same file-format-only knowledge as CmdFileLite,
/// standalone (no library reference, ontwerp-v0.1.md "Tech").
/// </summary>
public static class RstSummary
{
    /// <summary>A frontmatter block is a handful of lines; anything longer is a file that never
    /// closed its fence, and reading on would defeat the point of not reading the body.</summary>
    private const int MaxFrontmatterLines = 50;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>The summary, or null when the file is absent, has no frontmatter, has malformed
    /// YAML, or simply carries no summary: — the state every rst written before this convention
    /// existed is in. A null renders nothing, never an error.</summary>
    public static string? Read(string? path)
    {
        if (path is null || !File.Exists(path))
            return null;

        try
        {
            var yaml = ReadFrontmatter(path);
            if (yaml is null)
                return null;

            var dto = Deserializer.Deserialize<FrontmatterDto>(yaml) ?? new FrontmatterDto();
            return string.IsNullOrWhiteSpace(dto.Summary) ? null : dto.Summary.Trim();
        }
        catch (Exception)
        {
            return null; // unreadable file or invalid YAML — same "nothing to show" outcome
        }
    }

    /// <summary>The YAML between the opening and closing '---' fences, or null when the file does
    /// not open with one (or never closes it). Streams the file, so the body is never read.</summary>
    private static string? ReadFrontmatter(string path)
    {
        var yaml = new StringBuilder();
        var inside = false;
        var lines = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (!inside)
            {
                if (line.Trim().Length == 0)
                    continue; // leading blank lines are tolerated, same as CmdFileLite's reader
                if (line.TrimEnd() != "---")
                    return null;
                inside = true;
                continue;
            }

            if (line.TrimEnd() == "---")
                return yaml.ToString();
            if (++lines > MaxFrontmatterLines)
                return null;
            yaml.AppendLine(line);
        }

        return null; // fence never closed
    }

    private sealed class FrontmatterDto
    {
        public string? Summary { get; set; }
    }
}
