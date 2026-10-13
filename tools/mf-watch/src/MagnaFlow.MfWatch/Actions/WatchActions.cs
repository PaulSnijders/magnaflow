using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfWatch.Actions;

/// <summary>What mf-watch needs of an action: its name and its schedule. Neither set is manual only.</summary>
public sealed record WatchAction(string Name, int? EveryMinutes, TimeOnly? At)
{
    public bool IsScheduled => EveryMinutes is not null || At is not null;
}

/// <summary>
/// mf-watch's own reader of .magnaflow/actions.yml (docs/specs/concepts/actions.md). It reads only
/// the names and the schedule fields: what an action *does* is mf-worker's business, which also
/// validates the rest. Duplicated rather than referenced, like every other file format mf-watch
/// shares with the worker. Plus the two machine-local files per action it touches: the request
/// (consumed) and last.yml's `run_at` (read).
/// </summary>
public static class WatchActions
{
    private static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    public static string FilePath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "actions.yml");

    public static string Directory(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "actions");

    public static string RequestPath(string projectRoot, string name) => Path.Combine(Directory(projectRoot), $"{name}.request");

    public static (IReadOnlyList<WatchAction>? Actions, string? Error) Load(string projectRoot)
    {
        Dictionary<string, ActionDto?>? actions;
        try
        {
            actions = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<FileDto>(File.ReadAllText(FilePath(projectRoot)))?.Actions;
        }
        catch (Exception ex)
        {
            return (null, ex.Message.ReplaceLineEndings(" "));
        }
        if (actions is null)
            return (null, "no 'actions:' section");

        var result = new List<WatchAction>();
        foreach (var (name, a) in actions)
        {
            if (!NamePattern.IsMatch(name))
                return (null, $"action '{name}': name must match [a-z0-9][a-z0-9-]*");
            if (a?.EveryMinutes is not null && a.At is not null)
                return (null, $"action '{name}': every_minutes/at are both set (set at most one)");
            if (a?.EveryMinutes is <= 0)
                return (null, $"action '{name}': every_minutes must be a positive integer");
            TimeOnly? at = null;
            if (a?.At is not null)
            {
                if (!TimeOnly.TryParseExact(a.At.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    return (null, $"action '{name}': at '{a.At}' is not a time as HH:mm");
                at = parsed;
            }
            result.Add(new WatchAction(name, a?.EveryMinutes, at));
        }
        return (result, null);
    }

    /// <summary>The names of every pending request, in ordinal order.</summary>
    public static IReadOnlyList<string> PendingRequests(string projectRoot)
    {
        var dir = Directory(projectRoot);
        if (!System.IO.Directory.Exists(dir))
            return [];
        return [.. System.IO.Directory.EnumerateFiles(dir, "*.request")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>`run_at` from &lt;name&gt;.last.yml; null when missing or unreadable (never run).</summary>
    public static DateTimeOffset? LastRunAt(string projectRoot, string name)
    {
        var path = Path.Combine(Directory(projectRoot), $"{name}.last.yml");
        try
        {
            if (!File.Exists(path))
                return null;
            var dto = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<LastDto>(File.ReadAllText(path));
            return DateTimeOffset.TryParse(dto?.RunAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class FileDto
    {
        public Dictionary<string, ActionDto?>? Actions { get; set; }
    }

    private sealed class ActionDto
    {
        public int? EveryMinutes { get; set; }
        public string? At { get; set; }
    }

    private sealed class LastDto
    {
        public string? RunAt { get; set; }
    }
}
