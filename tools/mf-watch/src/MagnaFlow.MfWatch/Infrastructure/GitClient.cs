namespace MagnaFlow.MfWatch.Infrastructure;

/// <summary>Thin git orchestration via the git CLI, same style as MagnaFlow.WorkerController.Infrastructure.GitClient.</summary>
public sealed class GitClient(IProcessRunner processRunner, string projectRoot) : IGitClient
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    public async Task<bool> IsAvailableAsync() =>
        (await TryGitAsync("--version")).Succeeded;

    public async Task<bool> PullAsync()
    {
        var before = await RevParseHeadAsync();

        // --rebase explicitly, so the repo's own pull.rebase setting does not matter: a bare pull
        // refuses a diverged branch (docs/prompts/0025), a rebase replays only the commits that are
        // not on the remote yet. Nothing on the remote is ever rewritten.
        var pull = await TryGitAsync("pull", "--rebase", "--quiet");
        if (!pull.Succeeded)
        {
            if (await RebaseInProgressAsync())
            {
                var conflicts = await TryGitAsync("diff", "--name-only", "--diff-filter=U");
                var files = conflicts.StdOut.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var abort = await TryGitAsync("rebase", "--abort");
                if (!abort.Succeeded)
                    throw new GitException($"git pull --rebase stopped on a conflict and git rebase --abort failed (exit {abort.ExitCode}): {abort.StdErr.Trim()}");
                throw new GitConflictException(files,
                    $"git pull --rebase stopped on a conflict in {string.Join(", ", files)}; rebase aborted, tree unchanged");
            }
            throw new GitException($"git pull --rebase --quiet failed (exit {pull.ExitCode}): {pull.StdErr.Trim()}");
        }

        var after = await RevParseHeadAsync();
        return before != after;
    }

    public async Task<PushOutcome> PushAsync()
    {
        var result = await TryGitAsync("push", "--quiet");
        if (result.Succeeded)
            return PushOutcome.Pushed;

        // "! [rejected] main -> main (fetch first)" / "(non-fast-forward)". A hook's
        // "[remote rejected]" is not this and stays a plain failure.
        var output = result.StdErr + result.StdOut;
        if (output.Contains("[rejected]") && (output.Contains("(fetch first)") || output.Contains("(non-fast-forward)")))
            return PushOutcome.RejectedNonFastForward;

        throw new GitException($"git push --quiet failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
    }

    public async Task<string> SyncStateAsync()
    {
        var local = await TryGitAsync("rev-parse", "HEAD");
        var remote = await TryGitAsync("rev-parse", "@{u}");
        return $"{(local.Succeeded ? local.StdOut.Trim() : "?")}..{(remote.Succeeded ? remote.StdOut.Trim() : "?")}";
    }

    private async Task<bool> RebaseInProgressAsync()
    {
        foreach (var dir in new[] { "rebase-merge", "rebase-apply" })
        {
            var path = await TryGitAsync("rev-parse", "--git-path", dir);
            if (!path.Succeeded)
                continue;
            var full = Path.Combine(projectRoot, path.StdOut.Trim());
            if (Directory.Exists(full))
                return true;
        }
        return false;
    }

    private async Task<string> RevParseHeadAsync() =>
        (await GitAsync("rev-parse", "HEAD")).Trim();

    private async Task<string> GitAsync(params string[] args)
    {
        var result = await TryGitAsync(args);
        if (!result.Succeeded)
            throw new GitException($"git {string.Join(' ', args)} failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
        return result.StdOut;
    }

    private Task<ProcessResult> TryGitAsync(params string[] args) =>
        processRunner.RunExecutableAsync("git", args, projectRoot, timeout: GitTimeout);
}
