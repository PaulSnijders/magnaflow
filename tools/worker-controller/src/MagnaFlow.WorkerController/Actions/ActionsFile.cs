using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.WorkerController.Actions;

/// <summary>One validated entry under `actions:` (docs/specs/concepts/actions.md "The file").
/// Exactly one of Script and Prompt is set; at most one of EveryMinutes and At.</summary>
public sealed record ActionDefinition(
    string Name,
    string? Description,
    string? Script,
    IReadOnlyList<string> Args,
    string? Prompt,
    int? EveryMinutes,
    TimeOnly? At,
    TimeSpan Timeout);

/// <summary>
/// .magnaflow/actions.yml — a project's own named jobs. Optional: a project without the file has
/// no actions. Loaded the same way as ProjectConfig; every validation error names the action and
/// the field, so a human can fix the file from the message alone.
/// </summary>
public static class ActionsFile
{
    public static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);
    public const int DefaultTimeoutMinutes = 30;

    public static string PathFor(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "actions.yml");

    public static (IReadOnlyList<ActionDefinition>? Actions, string? Error) Load(string projectRoot)
    {
        var path = PathFor(projectRoot);
        if (!File.Exists(path))
            return (null, $"no actions file found at {path}");
        return Parse(File.ReadAllText(path), path);
    }

    public static (IReadOnlyList<ActionDefinition>? Actions, string? Error) Parse(string yaml, string path)
    {
        FileDto? dto;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            dto = deserializer.Deserialize<FileDto>(yaml);
        }
        catch (Exception ex)
        {
            return (null, $"invalid YAML in {path}: {ex.Message}");
        }

        if (dto?.Actions is null)
            return (null, $"{path} has no 'actions:' section");

        var problems = new List<string>();
        var actions = new List<ActionDefinition>();
        foreach (var (name, a) in dto.Actions)
        {
            var before = problems.Count;
            void Problem(string field, string message) => problems.Add($"action '{name}': {field} {message}");

            if (!NamePattern.IsMatch(name))
                Problem("name", "must match [a-z0-9][a-z0-9-]*");
            if (a is null)
            {
                Problem("script/prompt", "is missing (set exactly one)");
                continue;
            }

            var hasScript = !string.IsNullOrWhiteSpace(a.Script);
            var hasPrompt = !string.IsNullOrWhiteSpace(a.Prompt);
            if (hasScript == hasPrompt)
                Problem("script/prompt", hasScript ? "are both set (set exactly one)" : "is missing (set exactly one)");

            if (a.EveryMinutes is not null && a.At is not null)
                Problem("every_minutes/at", "are both set (set at most one)");
            if (a.EveryMinutes is <= 0)
                Problem("every_minutes", "must be a positive integer");

            TimeOnly? at = null;
            if (a.At is not null)
            {
                if (TimeOnly.TryParseExact(a.At.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    at = parsed;
                else
                    Problem("at", $"'{a.At}' is not a time as HH:mm");
            }

            if (a.TimeoutMinutes is <= 0)
                Problem("timeout_minutes", "must be a positive integer");

            if (problems.Count > before)
                continue;

            actions.Add(new ActionDefinition(
                name,
                string.IsNullOrWhiteSpace(a.Description) ? null : a.Description.Trim(),
                hasScript ? a.Script!.Trim() : null,
                a.Args ?? [],
                hasPrompt ? a.Prompt : null,
                a.EveryMinutes,
                at,
                TimeSpan.FromMinutes(a.TimeoutMinutes ?? DefaultTimeoutMinutes)));
        }

        return problems.Count > 0
            ? (null, $"{path}: {string.Join("; ", problems)}")
            : (actions, null);
    }

    private sealed class FileDto
    {
        public Dictionary<string, ActionDto?>? Actions { get; set; }
    }

    private sealed class ActionDto
    {
        public string? Description { get; set; }
        public string? Script { get; set; }
        public List<string>? Args { get; set; }
        public string? Prompt { get; set; }
        public int? EveryMinutes { get; set; }
        public string? At { get; set; }
        public int? TimeoutMinutes { get; set; }
    }
}
