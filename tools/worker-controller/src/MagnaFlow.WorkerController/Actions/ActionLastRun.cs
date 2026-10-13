using System.Globalization;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.WorkerController.Actions;

/// <summary>
/// .magnaflow/actions/&lt;name&gt;.last.yml: the last run, plus a refusal newer than that run
/// (docs/specs/concepts/actions.md "Files"). mf-watch reads `run_at` for what is due, the cockpit
/// shows the rest. Written through a temp file and a move, so a concurrent reader never sees half
/// a file.
/// </summary>
public sealed record ActionLastRun(
    DateTimeOffset? RunAt,
    string? Trigger,
    string? Result,
    int? DurationSeconds,
    DateTimeOffset? RefusedAt,
    string? Refused)
{
    public const string TimeFormat = "yyyy-MM-ddTHH:mm:sszzz";

    public static readonly ActionLastRun None = new(null, null, null, null, null, null);

    public static string PathFor(string projectRoot, string name) =>
        Path.Combine(ActionLog.Directory(projectRoot), $"{name}.last.yml");

    public static ActionLastRun Load(string projectRoot, string name)
    {
        var path = PathFor(projectRoot, name);
        if (!File.Exists(path))
            return None;
        try
        {
            var dto = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<Dto>(File.ReadAllText(path));
            if (dto is null)
                return None;
            return new ActionLastRun(ParseTime(dto.RunAt), dto.Trigger, dto.Result, dto.DurationSeconds,
                ParseTime(dto.RefusedAt), dto.Refused);
        }
        catch (Exception)
        {
            return None; // an unreadable file is as good as no run: the next write replaces it
        }
    }

    public void Save(string projectRoot, string name)
    {
        var text = new StringBuilder();
        if (RunAt is { } runAt)
        {
            text.Append("run_at: ").AppendLine(runAt.ToString(TimeFormat, CultureInfo.InvariantCulture));
            text.Append("trigger: ").AppendLine(Trigger);
            text.Append("result: ").AppendLine(Quote(Result));
            text.Append("duration_seconds: ").AppendLine(DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? "0");
        }
        if (RefusedAt is { } refusedAt)
        {
            text.Append("refused_at: ").AppendLine(refusedAt.ToString(TimeFormat, CultureInfo.InvariantCulture));
            text.Append("refused: ").AppendLine(Quote(Refused));
        }

        var path = PathFor(projectRoot, name);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private static string Quote(string? value) =>
        "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private sealed class Dto
    {
        public string? RunAt { get; set; }
        public string? Trigger { get; set; }
        public string? Result { get; set; }
        public int? DurationSeconds { get; set; }
        public string? RefusedAt { get; set; }
        public string? Refused { get; set; }
    }
}
