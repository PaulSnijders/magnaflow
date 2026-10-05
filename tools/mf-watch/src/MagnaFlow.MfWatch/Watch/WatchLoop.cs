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

        if (config.GitSync)
            activity |= await PullAsync();

        var commands = PromptStatusScanner.Scan(projectRoot, warning => log($"scan warning: {warning}"));
        NotifyNewStaleRunning(commands);

        foreach (var cmd in commands.Where(c => c.IsReady))
        {
            // Shutdown means no new dispatch; the poll still ends normally so the push below
            // carries whatever the worker that just finished committed.
            if (cancellationToken.IsCancellationRequested)
                break;
            await RunOneAsync(cmd);
            activity = true;
        }

        if (config.GitSync)
            await PushAsync();

        return activity;
    }

    private async Task<bool> PullAsync()
    {
        try
        {
            var pulled = await git.PullAsync();
            log(pulled ? "git pull: new commits" : "git pull: up to date");
            return pulled;
        }
        catch (GitException ex)
        {
            log($"git pull failed: {ex.Message}");
            await notifier.NotifyAsync("mf-watch: git pull failed", ex.Message);
            return false;
        }
    }

    private async Task PushAsync()
    {
        try
        {
            await git.PushAsync();
            log("git push: ok");
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
