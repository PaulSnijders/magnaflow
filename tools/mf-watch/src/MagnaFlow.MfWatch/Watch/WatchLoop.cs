using MagnaFlow.MfWatch.Actions;
using MagnaFlow.MfWatch.Config;
using MagnaFlow.MfWatch.Infrastructure;
using MagnaFlow.MfWatch.Notify;
using MagnaFlow.MfWatch.Polling;
using MagnaFlow.MfWatch.Prompts;

namespace MagnaFlow.MfWatch.Watch;

/// <summary>
/// One poll cycle (ontwerp-v0.1.md "What it does — and all it does"): pull, scan, run ready
/// commands sequentially, push, notify. mf-watch reads statuses and exit codes only — the
/// worker's own status transitions (running -> questions/done/aborted) are the sole source of
/// truth for what happened; mf-watch never guesses beyond re-reading the file it already knows
/// the id of.
/// </summary>
public sealed class WatchLoop(
    WatchConfig config,
    IGitClient git,
    IProcessRunner processes,
    string projectRoot,
    Action<string> log,
    INotifier notifier,
    IClock? clock = null,
    TimeZoneInfo? timeZone = null)
{
    private const string RefusedPrefix = "refused: ";

    private readonly IClock _clock = clock ?? new SystemClock();
    private readonly TimeZoneInfo _zone = timeZone ?? TimeZoneInfo.Local;

    // The baseline for an `at` action that has never run (docs/specs/concepts/actions.md): a new
    // machine must not fire a nightly job at noon.
    private readonly DateTimeOffset _startedAt = (clock ?? new SystemClock()).UtcNow;

    // Per-process memory for actions, like the pull notice: the refusal last logged per action (a
    // run past the guards clears it), request files whose delete failed (held open on Windows; the
    // next poll retries the delete, never the run), and the mtime of an actions.yml already
    // reported as invalid.
    private readonly Dictionary<string, string> _loggedRefusals = [];
    private readonly HashSet<string> _undeletedRequests = [];
    private DateTime? _invalidActionsFileMtime;

    /// <summary>The earliest time after the last poll at which a scheduled action falls due, left
    /// out: actions refused in that poll. Null without actions.yml or a scheduled action.</summary>
    public DateTimeOffset? NextActionDue { get; private set; }

    // In-memory only (v0.1 scope, no persistence): a stale `running` command is surfaced once per
    // mf-watch process lifetime, same philosophy as `questions` — the daemon does not nag on
    // every subsequent poll for something already handed to the human (ontwerp-v0.1.md "Guards").
    private readonly HashSet<string> _notifiedStaleIds = [];

    // The last pull failure that was notified (docs/prompts/0025): a diverged or unreachable remote
    // is said once, not every poll. Cleared by the next successful pull.
    private string? _pullFailureKey;

    /// <summary>True while a worker spawn is awaited — the Ctrl+C handler uses it to say a second
    /// Ctrl+C aborts the worker.</summary>
    public bool IsDispatching { get; private set; }

    /// <summary>The daemon: poll, sleep, repeat — or a single poll with <paramref name="once"/>.
    /// Cancellation stops the loop, never the worker: a poll in progress finishes its current
    /// dispatch (and its push), then the loop exits; a sleep is cut short at once.</summary>
    public async Task RunAsync(BackoffScheduler scheduler, bool once, CancellationToken cancellationToken)
    {
        while (true)
        {
            var activity = await PollOnceAsync(cancellationToken);
            scheduler.RecordPoll(activity);

            if (once || cancellationToken.IsCancellationRequested)
                return;

            var sleep = SleepFor(scheduler.Current);
            log($"sleeping {sleep}");
            try
            {
                // Sliced, not one long delay, so a wake file can cut the sleep short (Polling/WakeFile.cs).
                if (await WakeFile.SleepAsync(sleep, projectRoot, cancellationToken))
                    log("wake requested (.magnaflow/mf-watch.wake) — polling now");
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>The backoff interval, cut short to the next due action. The interval itself does
    /// not change: a scheduled action is not activity.</summary>
    public TimeSpan SleepFor(TimeSpan interval)
    {
        if (NextActionDue is not { } due)
            return interval;
        var until = TimeSpan.FromSeconds(Math.Ceiling((due - _clock.UtcNow).TotalSeconds));
        if (until <= TimeSpan.Zero)
            until = TimeSpan.FromSeconds(1);
        return until < interval ? until : interval;
    }

    /// <summary>Runs one full poll cycle. Returns true if anything happened (activity), which the
    /// caller feeds into the adaptive backoff.</summary>
    public async Task<bool> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        var activity = false;
        var synced = true;

        if (config.GitSync)
        {
            var pulled = await PullAsync();
            synced = pulled is not null;
            activity |= pulled == true;
        }

        var commands = PromptStatusScanner.Scan(projectRoot, warning => log($"scan warning: {warning}"));
        NotifyNewStaleRunning(commands);

        // A tree that could not be synced may be behind the remote: never dispatch on it
        // (docs/prompts/0025). The next poll whose pull succeeds picks the ready commands up.
        if (!synced)
            log("dispatch skipped: the working copy could not be synced");

        foreach (var cmd in commands.Where(c => synced && c.IsReady))
        {
            // Shutdown means no new dispatch; the poll still ends normally so the push below
            // carries whatever the worker that just finished committed.
            if (cancellationToken.IsCancellationRequested)
                break;
            await RunOneAsync(cmd);
            activity = true;
        }

        activity |= await RunActionsAsync(synced, cancellationToken);

        // No push after a failed pull either: a diverged push is only rejected, and its retry
        // would run into the same failure again. The next successful poll pushes.
        if (config.GitSync && synced)
            await PushAsync();

        return activity;
    }

    /// <summary>`git pull --rebase`. Returns whether HEAD moved, or null when the pull failed (the
    /// tree is not synced). A failure notifies once per distinct state (failure kind + local and
    /// upstream HEAD); the same state on later polls is one log line, no notification.</summary>
    private async Task<bool?> PullAsync()
    {
        try
        {
            var pulled = await git.PullAsync();
            log(pulled ? "git pull: new commits" : "git pull: up to date");
            _pullFailureKey = null;
            return pulled;
        }
        catch (GitException ex)
        {
            var conflict = ex as GitConflictException;
            var key = $"{(conflict is null ? "failed" : "conflict")} {await git.SyncStateAsync()}";
            if (key == _pullFailureKey)
            {
                log($"git pull still failing, state unchanged since the last notice: {ex.Message}");
                return null;
            }

            _pullFailureKey = key;
            if (conflict is not null)
            {
                log($"git pull --rebase: conflict in {string.Join(", ", conflict.Files)}; rebase aborted, tree unchanged");
                await notifier.NotifyAsync("mf-watch: git pull conflict",
                    $"rebase conflict in {string.Join(", ", conflict.Files)}; aborted, nothing dispatched until it is resolved by hand");
            }
            else
            {
                log($"git pull failed: {ex.Message}");
                await notifier.NotifyAsync("mf-watch: git pull failed", ex.Message);
            }
            return null;
        }
    }

    private async Task PushAsync()
    {
        try
        {
            if (await git.PushAsync() == PushOutcome.Pushed)
            {
                log("git push: ok");
                return;
            }

            // Someone else pushed in the meantime: replay our commits on top and try once more.
            log("git push: rejected (remote has new commits); pulling with rebase and retrying once");
            if (await PullAsync() is null)
                return;

            if (await git.PushAsync() == PushOutcome.Pushed)
            {
                log("git push: ok (after rebase)");
                return;
            }

            log("git push failed: rejected again after the rebase-pull");
            await notifier.NotifyAsync("mf-watch: git push failed", "rejected as non-fast-forward, also after a rebase-pull");
        }
        catch (GitException ex)
        {
            log($"git push failed: {ex.Message}");
            await notifier.NotifyAsync("mf-watch: git push failed", ex.Message);
        }
    }

    /// <summary>Poll step 5 (docs/specs/watch/mf-watch.md#one-poll), only when actions.yml exists:
    /// pending requests first (also on an unsynced tree: the human asked), then due scheduled
    /// actions (only on a synced tree), strictly one at a time. Returns whether HEAD moved across
    /// the step, which is the only way an action counts as activity.</summary>
    private async Task<bool> RunActionsAsync(bool synced, CancellationToken cancellationToken)
    {
        NextActionDue = null;
        var file = WatchActions.FilePath(projectRoot);
        if (!File.Exists(file))
            return false;

        var (actions, error) = WatchActions.Load(projectRoot);
        if (actions is null)
        {
            var mtime = File.GetLastWriteTimeUtc(file);
            if (mtime != _invalidActionsFileMtime)
            {
                _invalidActionsFileMtime = mtime;
                log($"actions.yml: {error}; actions skipped");
            }
        }
        else
        {
            _invalidActionsFileMtime = null;
        }

        string? headBefore = null;
        var spawned = false;
        var refusedNow = new HashSet<string>();
        async Task SpawnAsync(string name, string trigger)
        {
            if (!spawned)
            {
                headBefore = await git.HeadAsync();
                spawned = true;
            }
            if (await RunActionAsync(name, trigger))
                refusedNow.Add(name);
        }

        var known = actions?.Select(a => a.Name).ToList() ?? [];
        var pending = WatchActions.PendingRequests(projectRoot);
        _undeletedRequests.IntersectWith(pending);
        foreach (var name in pending.OrderBy(n => known.IndexOf(n) is var i and >= 0 ? i : int.MaxValue))
        {
            if (_undeletedRequests.Contains(name))
            {
                // Already ran; only its delete failed. Retry the delete, never the run.
                if (TryDeleteRequest(name))
                    _undeletedRequests.Remove(name);
                continue;
            }
            if (cancellationToken.IsCancellationRequested)
                break;

            if (actions is null)
                log($"[action {name}] request skipped: actions.yml is invalid");
            else if (!known.Contains(name))
                log($"[action {name}] request skipped: no such action in actions.yml");
            else
                await SpawnAsync(name, "manual");

            if (!TryDeleteRequest(name))
                _undeletedRequests.Add(name);
        }

        var scheduled = actions?.Where(a => a.IsScheduled).ToList() ?? [];
        if (synced)
        {
            foreach (var action in scheduled)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                if (ActionSchedule.IsDue(action, WatchActions.LastRunAt(projectRoot, action.Name), _startedAt, _clock.UtcNow, _zone))
                    await SpawnAsync(action.Name, "schedule");
            }
        }

        // A due time already past (refused, or skipped on an unsynced tree) waits for the next
        // regular poll: putting it in the minimum would spin the loop.
        var now = _clock.UtcNow;
        NextActionDue = scheduled
            .Where(a => !refusedNow.Contains(a.Name))
            .Select(a => ActionSchedule.NextDue(a, WatchActions.LastRunAt(projectRoot, a.Name), _startedAt, now, _zone))
            .Where(due => due > now)
            .Min();

        if (!spawned)
            return false;
        var headAfter = await git.HeadAsync();
        return headBefore is not null && headAfter is not null && headBefore != headAfter;
    }

    /// <summary>Spawns `mf-worker action` for one action. Returns true when it refused (exit 3).
    /// No spawn line of its own: the worker prints `started` once past its guards, so a refusal is
    /// exactly one log line, once per action and reason.</summary>
    private async Task<bool> RunActionAsync(string name, string trigger)
    {
        var args = new List<string> { "action", "--project", projectRoot, name, "--trigger", trigger };
        args.AddRange(config.WorkerArgs);

        ProcessResult result;
        IsDispatching = true;
        try
        {
            result = await processes.RunExecutableAsync(
                config.WorkerCommand, args, projectRoot,
                line =>
                {
                    if (!line.StartsWith(RefusedPrefix, StringComparison.Ordinal))
                        log($"[action {name}] {line}");
                },
                config.WorkerTimeout);
        }
        finally
        {
            IsDispatching = false;
        }

        if (!result.TimedOut && result.ExitCode == 3)
        {
            var reason = result.StdOut.Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith(RefusedPrefix, StringComparison.Ordinal))?[RefusedPrefix.Length..]
                ?? "exit 3";
            if (!_loggedRefusals.TryGetValue(name, out var logged) || logged != reason)
            {
                _loggedRefusals[name] = reason;
                log($"[action {name}] refused: {reason}");
            }
            return true;
        }

        _loggedRefusals.Remove(name);
        if (!result.TimedOut && result.ExitCode == 0)
        {
            log($"[action {name}] finished: ok");
            return false;
        }

        var what = result.TimedOut ? "timed out"
            : result.ExitCode == 1 ? "failed (exit 1)"
            : $"exited {result.ExitCode} — unexpected";
        log($"[action {name}] {what}");
        await notifier.NotifyAsync($"action {name}: failed", $"{what}; see .magnaflow/actions/{name}.log");
        return false;
    }

    private bool TryDeleteRequest(string name)
    {
        try
        {
            File.Delete(WatchActions.RequestPath(projectRoot, name));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // still held open by its writer: the next poll retries the delete
        }
    }

    private void NotifyNewStaleRunning(IReadOnlyList<ScannedCommand> commands)
    {
        foreach (var cmd in commands)
        {
            if (!cmd.IsRunning || !_notifiedStaleIds.Add(cmd.Id))
                continue;
            log($"[{cmd.Id}] found with status 'running' but mf-watch did not start it — a previous run may have been interrupted");
            _ = notifier.NotifyAsync($"{cmd.Id}: stale running", "a previous run may have been interrupted; inspect and reset status manually");
        }
    }

    // Deliberately no cancellation token: shutdown stops the loop (no new dispatch, no new poll),
    // never the in-flight worker — worker_timeout_minutes is its only bound.
    private async Task RunOneAsync(ScannedCommand cmd)
    {
        var title = cmd.Title ?? cmd.Id;
        log($"[{cmd.Id}] spawning worker");

        var args = new List<string> { "run", "--project", projectRoot, cmd.Id };
        args.AddRange(config.WorkerArgs);

        ProcessResult result;
        IsDispatching = true;
        try
        {
            result = await processes.RunExecutableAsync(
                config.WorkerCommand, args, projectRoot,
                line => log($"[{cmd.Id}] {line}"), config.WorkerTimeout);
        }
        finally
        {
            IsDispatching = false;
        }

        var after = PromptStatusScanner.ReadStatus(cmd.FilePath);
        _notifiedStaleIds.Add(cmd.Id); // whatever state it lands in, this run has already been observed

        if (result.ExitCode is 0 or 1 && after is "questions" or "done" or "aborted")
        {
            log($"[{cmd.Id}] worker finished (exit {result.ExitCode}); status is now '{after}'");
            if (after == "questions")
                await notifier.NotifyAsync($"{cmd.Id}: questions", $"{title} raised questions and needs a human answer");
            else
                await notifier.NotifyAsync($"{cmd.Id}: {after}", $"{title} finished ({after})");
            return;
        }

        var reason = result.TimedOut ? "timed out" : $"exited {result.ExitCode}";
        log($"[{cmd.Id}] worker {reason}; status is '{after ?? "<unreadable>"}' — unexpected");
        await notifier.NotifyAsync($"{cmd.Id}: error", $"worker {reason}; status is '{after ?? "<unreadable>"}' — inspect manually");
    }
}
