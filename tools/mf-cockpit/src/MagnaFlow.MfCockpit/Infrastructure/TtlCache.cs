namespace MagnaFlow.MfCockpit.Infrastructure;

/// <summary>
/// A tiny per-key, time-boxed memo for the only two endpoints that spawn processes:
/// GET .../git runs three `git` subprocesses and GET .../run spawns mf-run plus a port probe, and
/// both are re-read on every SSE event by every open page. Deliberately not a general cache layer —
/// one TTL fixed at construction, one entry per project, no eviction policy, no size bound, no
/// serialization. Writes that change what it caches call <see cref="Invalidate"/> so a button's own
/// status read never shows the state from before the click.
/// </summary>
public sealed class TtlCache<T>(IClock clock, TimeSpan ttl)
{
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly object _gate = new();

    private sealed record Entry(Task<T> Value, DateTimeOffset ExpiresAt);

    /// <summary>The cached value for this key while it is fresh, otherwise the result of
    /// <paramref name="factory"/>, cached for the TTL. The in-flight Task is what gets stored, so
    /// two concurrent requests for the same project share one spawn instead of racing.</summary>
    public Task<T> GetOrAddAsync(string key, Func<Task<T>> factory)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached) && cached.ExpiresAt > clock.UtcNow)
                return cached.Value;

            // factory() is started under the lock on purpose: releasing first would let a second
            // caller miss too and spawn a duplicate process, which is the whole cost being avoided.
            var task = factory();
            _entries[key] = new Entry(task, clock.UtcNow + ttl);

            // A failure is never worth remembering for the rest of the TTL — the next request
            // should retry the process, not replay the exception.
            _ = task.ContinueWith(
                _ => Forget(key, task),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return task;
        }
    }

    /// <summary>Drops this key's entry, so the next read recomputes. Called by the write actions
    /// that change what is cached (commit-all, pull, run start/stop/restart).</summary>
    public void Invalidate(string key)
    {
        lock (_gate)
            _entries.Remove(key);
    }

    /// <summary>Removes an entry only if it still holds the given task — a retry that already
    /// replaced it must not be thrown away by the loser's continuation.</summary>
    private void Forget(string key, Task<T> task)
    {
        lock (_gate)
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value, task))
                _entries.Remove(key);
    }
}
