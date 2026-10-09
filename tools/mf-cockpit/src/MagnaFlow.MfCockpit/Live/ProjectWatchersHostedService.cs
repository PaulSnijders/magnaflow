using System.Collections.Concurrent;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using Microsoft.Extensions.Hosting;

namespace MagnaFlow.MfCockpit.Live;

/// <summary>Lets write #6 (add project) start a new project's FileSystemWatcher at once, with no
/// restart, and write #7 (remove project) dispose exactly the removed project's watcher — no orphan
/// watcher, no SSE from a project the cockpit has forgotten (ontwerp-v0.5.md item 4).</summary>
public interface IProjectWatcherRegistry
{
    void Add(string projectName, string projectPath);
    void Remove(string projectName);
}

/// <summary>Owns one ProjectWatcher per configured project for the app's lifetime, keyed by name so
/// a single one can be disposed when its project is unregistered (write #7).</summary>
public sealed class ProjectWatchersHostedService(CockpitConfig config, SseHub hub, TtlCache<RunStatusDto> runCache) : IHostedService, IProjectWatcherRegistry
{
    private ProjectWatcher Create(string projectName, string projectPath) =>
        new(projectName, projectPath, hub, onStableState: () => runCache.Invalidate(projectName));

    private readonly ConcurrentDictionary<string, ProjectWatcher> _watchers = new();
    private int _stopped;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var project in config.Projects)
            _watchers[project.Name] = Create(project.Name, project.Path);
        return Task.CompletedTask;
    }

    public void Add(string projectName, string projectPath)
    {
        // After shutdown began, a late add must not leave an undisposed watcher behind.
        if (Volatile.Read(ref _stopped) != 0)
            return;
        var watcher = Create(projectName, projectPath);
        if (!_watchers.TryAdd(projectName, watcher))
            watcher.Dispose();
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
