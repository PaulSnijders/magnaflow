namespace MagnaFlow.MfCockpit.Watch;

public sealed record WatchControlResult(bool Success, string? Error);

/// <summary>The watch toggle (project.html's "Add project"-adjacent switch): turns mf-watch on/off
/// for one project. Two implementations, selected once at startup by OS (Program.cs) — Linux
/// already has real per-project supervision via the mf-watch@.service systemd template
/// (tools/install/install.sh); Windows has none, so the cockpit spawns/kills mf-watch itself. See
/// docs/fase5-cockpit/watch-toggle-notes.md.</summary>
public interface IWatchControl
{
    Task<bool> IsRunningAsync(string projectRoot, CancellationToken cancellationToken = default);

    Task<WatchControlResult> StartAsync(string projectRoot, CancellationToken cancellationToken = default);

    Task<WatchControlResult> StopAsync(string projectRoot, CancellationToken cancellationToken = default);
}
