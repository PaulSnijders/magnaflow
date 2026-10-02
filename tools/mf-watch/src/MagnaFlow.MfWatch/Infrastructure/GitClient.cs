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
        await GitAsync("pull", "--quiet");
        var after = await RevParseHeadAsync();
        return before != after;
    }

    public async Task PushAsync() =>
        await GitAsync("push", "--quiet");

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
