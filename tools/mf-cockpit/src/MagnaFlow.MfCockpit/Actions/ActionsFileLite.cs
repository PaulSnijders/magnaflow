using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfCockpit.Actions;

/// <summary>One action as the Actions card shows it. Schedule is display text: `every N min`,
/// `daily HH:mm` or `manual`.</summary>
public sealed record ActionLite(string Name, string? Description, string Kind, string Schedule);

/// <summary>`.magnaflow/actions/&lt;name&gt;.last.yml` as written by mf-worker: the last run, plus a
/// refusal newer than that run. Times are passed through as written.</summary>
public sealed record ActionLastLite(string? RunAt, string? Trigger, string? Result, int? DurationSeconds, string? RefusedAt, string? Refused);

/// <summary>
/// The cockpit's reader of .magnaflow/actions.yml (docs/specs/concepts/actions.md "The file"):
/// the same validation as mf-worker's ActionsFile, so the card shows the same error the worker
/// would give. Duplicated rather than referenced, like every other file format the cockpit reads.
/// Names are what the Run endpoint turns into file paths: only a name in a parsed file is ever
/// used for one, and the pattern keeps it to one plain path segment.
/// </summary>
public static class ActionsFileLite
{
    private static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    public static string FilePath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "actions.yml");

    public static string Directory(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "actions");

    public static string RequestPath(string projectRoot, string name) => Path.Combine(Directory(projectRoot), $"{name}.request");

    public static string LogPath(string projectRoot, string name) => Path.Combine(Directory(projectRoot), $"{name}.log");

    /// <summary>Null actions with a null error: no file. Null actions with an error: it does not parse.</summary>
    public static (IReadOnlyList<ActionLite>? Actions, string? Error) Load(string projectRoot)
    {
        var path = FilePath(projectRoot);
        if (!File.Exists(path))
            return (null, null);

        Dictionary<string, ActionDto?>? actions;
        try
        {
            actions = Deserializer().Deserialize<FileDto>(File.ReadAllText(path))?.Actions;
        }
        catch (Exception ex)
        {
            return (null, $"invalid YAML: {ex.Message}");
        }
        if (actions is null)
            return (null, "no 'actions:' section");

        var problems = new List<string>();
        var result = new List<ActionLite>();
        foreach (var (name, a) in actions)
        {
            var before = problems.Count;
            void Problem(string field, string message) => problems.Add($"action '{name}': {field} {message}");

            if (!NamePattern.IsMatch(name))
                Problem("name", "must match [a-z0-9][a-z0-9-]*");
            var hasScript = !string.IsNullOrWhiteSpace(a?.Script);
            var hasPrompt = !string.IsNullOrWhiteSpace(a?.Prompt);
            if (hasScript == hasPrompt)
                Problem("script/prompt", hasScript ? "are both set (set exactly one)" : "is missing (set exactly one)");
            if (a?.EveryMinutes is not null && a.At is not null)
                Problem("every_minutes/at", "are both set (set at most one)");
            if (a?.EveryMinutes is <= 0)
                Problem("every_minutes", "must be a positive integer");
            if (a?.At is not null && !TimeOnly.TryParseExact(a.At.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                Problem("at", $"'{a.At}' is not a time as HH:mm");
            if (a?.TimeoutMinutes is <= 0)
                Problem("timeout_minutes", "must be a positive integer");
            if (problems.Count > before)
                continue;

            var schedule = a!.EveryMinutes is { } n ? $"every {n} min" : a.At is { } at ? $"daily {at.Trim()}" : "manual";
            result.Add(new ActionLite(name, string.IsNullOrWhiteSpace(a.Description) ? null : a.Description.Trim(),
                hasScript ? "script" : "prompt", schedule));
        }

        return problems.Count > 0 ? (null, string.Join("; ", problems)) : (result, null);
    }

    /// <summary>Null when the action never ran nor was refused, or the file cannot be read.</summary>
    public static ActionLastLite? LoadLast(string projectRoot, string name)
    {
        var path = Path.Combine(Directory(projectRoot), $"{name}.last.yml");
        try
        {
            if (!File.Exists(path))
                return null;
            var dto = Deserializer().Deserialize<LastDto>(File.ReadAllText(path));
            return dto is null ? null : new ActionLastLite(dto.RunAt, dto.Trigger, dto.Result, dto.DurationSeconds, dto.RefusedAt, dto.Refused);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IDeserializer Deserializer() => new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private sealed class FileDto
    {
        public Dictionary<string, ActionDto?>? Actions { get; set; }
    }

    private sealed class ActionDto
    {
        public string? Description { get; set; }
        public string? Script { get; set; }
        public string? Prompt { get; set; }
        public int? EveryMinutes { get; set; }
        public string? At { get; set; }
        public int? TimeoutMinutes { get; set; }
    }

    private sealed class LastDto
    {
        public string? RunAt { get; set; }
        public string? Trigger { get; set; }
        public string? Result { get; set; }
        public int? DurationSeconds { get; set; }
        public string? RefusedAt { get; set; }
        public string? Refused { get; set; }
    }
}
