using System.Globalization;

namespace MagnaFlow.MfRun.Runtime;

/// <summary>
/// .magnaflow/run/&lt;service&gt;.pid — plain text, PID on the first line and the process start
/// time (round-trip ISO-8601) on the second, so `cat` suffices (ontwerp-v0.1.md "PID files"). The
/// start time is what the PID-reuse guard compares against a currently-running process's own
/// start time — a match means "still the same process", anything else means "stale".
/// </summary>
public static class PidFile
{
    public static string PathFor(string projectRoot, string serviceName) =>
        Path.Combine(projectRoot, ".magnaflow", "run", $"{serviceName}.pid");

    public static void Write(string path, int pid, DateTimeOffset startTimeUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var content = $"{pid}\n{startTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\n";
        File.WriteAllText(path, content);
    }

    /// <summary>Null if the file is missing or not in the expected two-line shape.</summary>
    public static PidFileEntry? TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            return null;
        if (!int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
            return null;
        if (!DateTimeOffset.TryParse(lines[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startTime))
            return null;

        return new PidFileEntry(pid, startTime);
    }

    public static void Delete(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* best effort */ }
    }
}

public sealed record PidFileEntry(int Pid, DateTimeOffset StartTimeUtc);
