namespace MagnaFlow.WorkerController.Infrastructure;

public sealed class GitException(string message) : Exception(message);

public interface IGitClient
{
    Task<bool> IsAvailableAsync();
    Task<bool> IsWorkingTreeCleanAsync();
    Task<string> GetCurrentBranchAsync();
    Task<string?> GetDefaultBranchAsync();
    Task<bool> BranchExistsAsync(string branch);
    Task CheckoutAsync(string branch);
    Task CreateBranchAsync(string branch, string fromBase);
    Task StagePathAsync(string relativePath);
    /// <summary>
    /// Stages bookkeeping evidence: a path the project does not have, or deliberately ignores,
    /// is quietly skipped instead of failing the run. Any other git failure still throws.
    /// </summary>
    Task StageIfPresentAsync(string relativePath);
    Task StageAllExceptBookkeepingAsync();
    Task<bool> HasStagedChangesAsync();
    Task CommitAsync(string message);
    /// <summary>The commit SHA at HEAD.</summary>
    Task<string> GetHeadCommitAsync();
    /// <summary>True when the commit is reachable from a remote-tracking ref — rewriting it would mean a force-push.</summary>
    Task<bool> IsOnRemoteTrackingRefAsync(string commit);
    /// <summary>Replaces HEAD with a commit of everything currently staged, under a new message.</summary>
    Task AmendCommitAsync(string message);
    /// <summary>The remote to push to ("origin" if present, else the first configured remote), or null if none.</summary>
    Task<string?> GetRemoteAsync();
    Task PushAsync(string remote, string branch);
}

/// <summary>
/// Thin git orchestration via the git CLI (constitution V: orchestrate existing tools).
/// All commands run in the target project root.
/// </summary>
public sealed class GitClient(IProcessRunner processRunner, string projectRoot) : IGitClient
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    public async Task<bool> IsAvailableAsync() =>
        (await TryGitAsync("--version")).Succeeded;

    public async Task<bool> IsWorkingTreeCleanAsync() =>
        (await GitAsync("status", "--porcelain")).Trim().Length == 0;

    public async Task<string> GetCurrentBranchAsync()
    {
        var branch = (await GitAsync("branch", "--show-current")).Trim();
        if (branch.Length == 0)
            throw new GitException("not on a branch (detached HEAD?) — check out a branch first");
        return branch;
    }

    public async Task<string?> GetDefaultBranchAsync()
    {
        var originHead = await TryGitAsync("symbolic-ref", "--short", "refs/remotes/origin/HEAD");
        if (originHead.Succeeded)
        {
            var name = originHead.StdOut.Trim();
            var slash = name.IndexOf('/');
            if (slash >= 0) name = name[(slash + 1)..];
            if (name.Length > 0) return name;
        }
        // Local-first repos usually have no origin: fall back to conventional names.
        if (await BranchExistsAsync("main")) return "main";
        if (await BranchExistsAsync("master")) return "master";
        return null;
    }

    public async Task<bool> BranchExistsAsync(string branch) =>
        (await TryGitAsync("rev-parse", "--verify", "--quiet", $"refs/heads/{branch}")).Succeeded;

    public async Task CheckoutAsync(string branch) =>
        await GitAsync("checkout", "--quiet", branch);

    public async Task CreateBranchAsync(string branch, string fromBase) =>
        await GitAsync("checkout", "--quiet", "-b", branch, fromBase);

    public async Task StagePathAsync(string relativePath) =>
        await GitAsync("add", "--", relativePath.Replace('\\', '/'));

    /// <summary>
    /// The two ways `git add` refuses a bookkeeping path are both the project's business, not a
    /// broken repo: the evidence folder was never written (exit 128, "pathspec did not match any
    /// files"), or the project ignores .magnaflow/ (exit 1, "the following paths are ignored").
    /// Neither may cost the run its terminal commit, so both are checked up front and skipped.
    /// Everything else still throws through GitAsync.
    /// </summary>
    public async Task StageIfPresentAsync(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        var absolute = Path.Combine(projectRoot, path);
        if (!File.Exists(absolute) && !Directory.Exists(absolute))
            return;
        // check-ignore: exit 0 = ignored, 1 = not ignored; anything else is a question we could
        // not ask, and then `git add` itself is the better judge.
        if ((await TryGitAsync("check-ignore", "-q", "--", path)).ExitCode == 0)
            return;
        await GitAsync("add", "--", path);
    }

    public async Task StageAllExceptBookkeepingAsync() =>
        await GitAsync("add", "-A", "--", ".", ":(exclude).magnaflow", ":(exclude)docs/prompts");

    public async Task<bool> HasStagedChangesAsync() =>
        !(await TryGitAsync("diff", "--cached", "--quiet")).Succeeded; // exit 1 = staged changes exist

    public async Task CommitAsync(string message) =>
        await GitAsync("commit", "--quiet", "-m", message);

    public async Task<string> GetHeadCommitAsync() =>
        (await GitAsync("rev-parse", "HEAD")).Trim();

    public async Task<bool> IsOnRemoteTrackingRefAsync(string commit)
    {
        var contains = await TryGitAsync("branch", "--remotes", "--contains", commit);
        // A question we cannot answer counts as "yes": the only thing riding on it is an amend.
        return !contains.Succeeded || contains.StdOut.Trim().Length > 0;
    }

    public async Task AmendCommitAsync(string message) =>
        await GitAsync("commit", "--quiet", "--amend", "-m", message);

    public async Task<string?> GetRemoteAsync()
    {
        var remotes = (await GitAsync("remote"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (remotes.Length == 0) return null;
        return remotes.Contains("origin") ? "origin" : remotes[0];
    }

    public async Task PushAsync(string remote, string branch) =>
        await GitAsync("push", "--quiet", "--set-upstream", remote, branch);

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
