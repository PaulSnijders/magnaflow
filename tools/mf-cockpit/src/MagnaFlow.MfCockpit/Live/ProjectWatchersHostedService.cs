using System.Collections.Concurrent;
using MagnaFlow.MfCockpit.Config;
using Microsoft.Extensions.Hosting;

namespace MagnaFlow.MfCockpit.Live;

/// <summary>Lets write #7 (remove project) dispose exactly the removed project's FileSystemWatcher
/// — no orphan watcher, no SSE from a project the cockpit has forgotten (ontwerp-v0.5.md item 4).</summary>
public interface IProjectWatcherRegistry
{
    void Remove(string projectName);
}

/// <summary>Owns one ProjectWatcher per configured project for the app's lifetime, keyed by name so
/// a single one can be disposed when its project is unregistered (write #7).</summary>
public sealed class ProjectWatchersHostedService(CockpitConfig config, SseHub hub) : IHostedService, IProjectWatcherRegistry
{
    private readonly ConcurrentDictionary<string, ProjectWatcher> _watchers = new();
    private int _stopped;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var project in config.Projects)
            _watchers[project.Name] = new ProjectWatcher(project.Name, project.Path, hub);
        return Task.CompletedTask;
    }

    public void Remove(string projectName)
    {
        if (_watchers.TryRemove(projectName, out var watcher))
            watcher.Dispose();
    }

    // The generic host can invoke StopAsync more than once during shutdown (e.g. an explicit
    // Host.StopAsync followed by Dispose's own stop-if-not-stopped path) — guard against that
    // re-entering the same collection concurrently (observed as a "Collection was modified"
    // exception under WebApplicationFactory's disposal).
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return Task.CompletedTask;

        foreach (var watcher in _watchers.Values)
            watcher.Dispose();
        _watchers.Clear();
        return Task.CompletedTask;
    }
}
