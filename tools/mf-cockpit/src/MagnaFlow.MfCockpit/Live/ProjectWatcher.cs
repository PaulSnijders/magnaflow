using System.Collections.Concurrent;

namespace MagnaFlow.MfCockpit.Live;

/// <summary>
/// One FileSystemWatcher pair per project — docs/prompts/ (kind "lane") and .magnaflow/ (kind
/// "watch" for mf-watch.log/.lock at its root, "evidence" for anything nested under
/// .magnaflow/&lt;id&gt;/) — debounced ~500ms and fanned into SseHub (ontwerp-v0.1.md "Live updates").
/// A project missing one of these directories at startup (fresh project, nothing run yet) simply
/// isn't watched for that kind until the cockpit restarts — v0.1 scope, same "no correctness at
/// stake, just a stale page" tradeoff the design doc accepts for this whole mechanism.
/// </summary>
public sealed class ProjectWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly SseHub _hub;
    private readonly string _projectName;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, Timer> _timers = new();

    public ProjectWatcher(string projectName, string projectRoot, SseHub hub)
    {
        _projectName = projectName;
        _hub = hub;

        TryWatch(Path.Combine(projectRoot, "docs", "prompts"), includeSubdirectories: false, _ => "lane");
        TryWatch(Path.Combine(projectRoot, ".magnaflow"), includeSubdirectories: true, ClassifyMagnaflow);
    }

    private static string ClassifyMagnaflow(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (fileName is "mf-watch.log" or "mf-watch.lock")
            return "watch";

        // .magnaflow/run/ (PID files, service logs — ontwerp-v0.3.md "SSE": "a new coarse kind
        // run") — catches both a change to the directory itself and to anything inside it.
        var parentName = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? "");
        return fileName == "run" || parentName == "run" ? "run" : "evidence";
    }

    private void TryWatch(string dir, bool includeSubdirectories, Func<string, string> classify)
    {
        if (!Directory.Exists(dir))
            return;

        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = includeSubdirectories };
        }
        catch (IOException)
        {
            return; // directory disappeared between the Exists check and construction
        }

        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
        watcher.Changed += (_, e) => Debounced(classify(e.FullPath));
        watcher.Created += (_, e) => Debounced(classify(e.FullPath));
        watcher.Deleted += (_, e) => Debounced(classify(e.FullPath));
        watcher.Renamed += (_, e) => Debounced(classify(e.FullPath));
        watcher.Error += (_, _) => { }; // buffer overflow etc. — best-effort mechanism, never fatal
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void Debounced(string kind)
    {
        _timers.AddOrUpdate(kind,
            _ => new Timer(_ => Fire(kind), null, Debounce, Timeout.InfiniteTimeSpan),
            (_, existing) =>
            {
                existing.Change(Debounce, Timeout.InfiniteTimeSpan);
                return existing;
            });
    }

    private void Fire(string kind) => _hub.Broadcast(new LiveEvent(_projectName, kind));

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        foreach (var timer in _timers.Values)
            timer.Dispose();
    }
}
