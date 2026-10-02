using MagnaFlow.MfCockpit.Config;

namespace MagnaFlow.MfCockpit.Watch;

/// <summary>Windows has no per-project mf-watch supervision (install.ps1's shortcut starts exactly
/// one hardcoded instance) — so here the cockpit spawns/kills mf-watch itself, reading PID/start
/// time back from mf-watch's own lock file (MfWatchLockFile) rather than tracking a second, its
/// own PID record: works for "is it running" regardless of how that instance was started (this
/// toggle, the desktop shortcut, or by hand), and survives a cockpit restart between clicks since
/// nothing lives in memory. Known gap (see watch-toggle-notes.md): an instance spawned this way
/// does not survive a reboot/logout — only the desktop shortcut's single hardcoded instance does,
/// until install.ps1 grows per-project scheduled-task support.</summary>
public sealed class WindowsWatchControl(WatchClientConfig config, IWatchProcessSpawner spawner) : IWatchControl
{
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    public Task<bool> IsRunningAsync(string projectRoot, CancellationToken cancellationToken = default) =>
        Task.FromResult(IsRunning(projectRoot));

    public Task<WatchControlResult> StartAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        if (IsRunning(projectRoot))
            return Task.FromResult(new WatchControlResult(true, null));

        try
        {
            spawner.Start(config.Command, ["--project", projectRoot], projectRoot);
            return Task.FromResult(new WatchControlResult(true, null));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(new WatchControlResult(false, ex.Message));
        }
    }

    public Task<WatchControlResult> StopAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var entry = MfWatchLockFile.TryRead(projectRoot);
        if (entry is null)
            return Task.FromResult(new WatchControlResult(true, null)); // not running — idempotent

        var live = spawner.GetProcess(entry.Pid);
        if (live is not null && IsSameProcess(live, entry))
            spawner.Kill(entry.Pid);

        return Task.FromResult(new WatchControlResult(true, null));
    }

    private bool IsRunning(string projectRoot)
    {
        var entry = MfWatchLockFile.TryRead(projectRoot);
        if (entry is null)
            return false;

        var live = spawner.GetProcess(entry.Pid);
        return live is not null && IsSameProcess(live, entry);
    }

    private static bool IsSameProcess(WatchProcessSnapshot live, MfWatchLockEntry entry) =>
        (live.StartTimeUtc - entry.StartTimeUtc).Duration() <= StartTimeTolerance;
}
