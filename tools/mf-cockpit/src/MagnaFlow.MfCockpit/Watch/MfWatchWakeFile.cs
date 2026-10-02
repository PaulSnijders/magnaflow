namespace MagnaFlow.MfCockpit.Watch;

/// <summary>Write-side knowledge of mf-watch's own <c>.magnaflow/mf-watch.wake</c> signal
/// (MagnaFlow.MfWatch.Polling.WakeFile): an empty file asking a *running* watcher to poll now
/// instead of finishing its backed-off sleep. Duplicated rather than referenced, same decoupling
/// every cockpit reader keeps toward the other tools.
///
/// Deliberately not on <see cref="IWatchControl"/>: that seam exists because starting and stopping
/// a watcher is genuinely different on systemd and on Windows. Creating an empty file is not — so
/// this needs no platform split, and `touch`/`New-Item` is the by-hand equivalent on either.</summary>
public static class MfWatchWakeFile
{
    public static string PathFor(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "mf-watch.wake");

    /// <summary>Creates the (empty) wake file, leaving one already there untouched — an unconsumed
    /// wake is already pending, so writing it again would say nothing new.</summary>
    public static void Write(string projectRoot)
    {
        var path = PathFor(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
            File.WriteAllText(path, "");
    }
}
