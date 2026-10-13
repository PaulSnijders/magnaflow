using System.Globalization;
using System.Text;

namespace MagnaFlow.WorkerController.Actions;

/// <summary>
/// .magnaflow/actions/&lt;name&gt;.log: every run of one action, appended (docs/specs/concepts/actions.md
/// "Files"). UTF-8 without a BOM, one header per run, the merged output, then a result line.
/// </summary>
public static class ActionLog
{
    public static string Directory(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "actions");

    public static string PathFor(string projectRoot, string name) => Path.Combine(Directory(projectRoot), $"{name}.log");

    public static StreamWriter OpenAppend(string projectRoot, string name)
    {
        System.IO.Directory.CreateDirectory(Directory(projectRoot));
        var stream = new FileStream(PathFor(projectRoot, name), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static string Header(string name, string trigger, DateTimeOffset start) =>
        $"=== action {name} ({trigger}) — {start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)} ===";

    public static string ResultLine(string result, int seconds) => $"=== result: {result} — {seconds}s ===";
}
