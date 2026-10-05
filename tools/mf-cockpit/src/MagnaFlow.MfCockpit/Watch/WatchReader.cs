using MagnaFlow.MfCockpit.Evidence;

namespace MagnaFlow.MfCockpit.Watch;

public sealed record WatchStatus(TailResult Log, bool LockPresent);

/// <summary>
/// Reads mf-watch's own diary (docs/decisions/0006-mf-watch-design.md "Observability"):
/// .magnaflow/mf-watch.log (tail) and .magnaflow/mf-watch.lock (presence = "watcher alive on this
/// copy"). Read-only, no coupling to MagnaFlow.MfWatch — same file-format-only knowledge as every
/// other cockpit reader.
/// </summary>
public static class WatchReader
{
    public static WatchStatus Read(string projectRoot, int maxLines = TailReader.DefaultMaxLines)
    {
        var logPath = Path.Combine(projectRoot, ".magnaflow", "mf-watch.log");
        var lockPath = Path.Combine(projectRoot, ".magnaflow", "mf-watch.lock");
        return new WatchStatus(TailReader.Read(logPath, maxLines), File.Exists(lockPath));
    }
}
