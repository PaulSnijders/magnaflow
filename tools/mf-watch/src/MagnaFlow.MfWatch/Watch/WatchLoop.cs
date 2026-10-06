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
    INotifier notifier)
{
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

            log($"sleeping {scheduler.Current}");
            try
            {
                // Sliced, not one long delay, so a wake file can cut the sleep short (Polling/WakeFile.cs).
                if (await WakeFile.SleepAsync(scheduler.Current, projectRoot, cancellationToken))
                    log("wake requested (.magnaflow/mf-watch.wake) — polling now");
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
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
