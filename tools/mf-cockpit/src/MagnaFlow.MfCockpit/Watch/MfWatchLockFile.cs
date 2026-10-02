using System.Globalization;

namespace MagnaFlow.MfCockpit.Watch;

/// <summary>Read-only knowledge of mf-watch's own .magnaflow/mf-watch.lock format (PID on line
/// one, start time round-trip ISO-8601 on line two — MagnaFlow.MfWatch.Runtime.InstanceLock,
/// opened with FileShare.Read specifically so this file can read it). Duplicated rather than
/// referenced, same decoupling every cockpit reader keeps toward the other tools.</summary>
public sealed record MfWatchLockEntry(int Pid, DateTimeOffset StartTimeUtc);

public static class MfWatchLockFile
{
    public static string PathFor(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "mf-watch.lock");

    /// <summary>Null if missing, still exclusively held for reads too (shouldn't happen — mf-watch
    /// opens with FileShare.Read), or not in the expected two-line shape.</summary>
    public static MfWatchLockEntry? TryRead(string projectRoot)
    {
        var path = PathFor(projectRoot);
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2)
                return null;
            if (!int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                return null;
            if (!DateTimeOffset.TryParse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startTime))
                return null;

            return new MfWatchLockEntry(pid, startTime);
        }
        catch (IOException)
        {
            return null; // held exclusively against reads too, or vanished mid-read — treat as unknown
        }
    }
}
