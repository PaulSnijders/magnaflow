using MagnaFlow.MfWatch.Config;
using MagnaFlow.MfWatch.Infrastructure;
using MagnaFlow.MfWatch.Watch;

namespace MagnaFlow.MfWatch.Tests;

/// <summary>
/// git_sync against the real git CLI (docs/prompts/0025): a bare "GitHub", the watcher's clone and
/// a second "desktop" clone that pushes in between. Faking git here would only test the
/// assumption about what `pull --rebase` and a stopped rebase look like, not the behaviour.
/// </summary>
public class GitSyncRealGitTests : IDisposable
{
    private readonly TempProject _dir = new();
    private readonly string _remote;
    private readonly string _watcher;
    private readonly string _desktop;

    public GitSyncRealGitTests()
    {
        _remote = Path.Combine(_dir.Root, "remote.git");
        _watcher = Path.Combine(_dir.Root, "watcher");
        _desktop = Path.Combine(_dir.Root, "desktop");

        Git(_dir.Root, "init", "--quiet", "--bare", "--initial-branch=main", _remote);
        Git(_dir.Root, "clone", "--quiet", _remote, _watcher);
        Configure(_watcher);
        Commit(_watcher, "shared.md", "line one\n", "seed");
        Git(_watcher, "push", "--quiet", "-u", "origin", "main");
        Git(_dir.Root, "clone", "--quiet", _remote, _desktop);
        Configure(_desktop);
    }

    public void Dispose() => _dir.Dispose();

    private static void Configure(string repo)
    {
        Git(repo, "config", "user.email", "test@example.com");
        Git(repo, "config", "user.name", "mf-tests");
        Git(repo, "config", "pull.rebase", "false"); // the explicit --rebase must win over this
    }

    private static string Git(string cwd, params string[] args)
    {
        var result = new CliWrapProcessRunner()
            .RunExecutableAsync("git", args, cwd, timeout: TimeSpan.FromSeconds(30))
            .GetAwaiter().GetResult();
        Assert.True(result.Succeeded, $"git {string.Join(' ', args)}: {result.StdErr}");
        return result.StdOut.Trim();
    }

    private static void Commit(string repo, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(repo, file), content);
        Git(repo, "add", "-A");
        Git(repo, "commit", "--quiet", "-m", message);
    }

    private WatchLoop Loop(FakeNotifier notifier, FakeProcessRunner workers, List<string>? log = null) =>
        new(new WatchConfig { GitSync = true }, new GitClient(new CliWrapProcessRunner(), _watcher),
            workers, _watcher, (log ?? []).Add, notifier);

    [Fact]
    public async Task DivergedCloneWithNonOverlappingCommitsIsReconciledByPullAndPush()
    {
        Commit(_watcher, "worker.md", "from the worker\n", "worker commit");
        Commit(_desktop, "desktop.md", "from the desktop\n", "desktop commit");
        Git(_desktop, "push", "--quiet");
        var desktopHead = Git(_desktop, "rev-parse", "HEAD");
        var notifier = new FakeNotifier();

        var activity = await Loop(notifier, new FakeProcessRunner()).PollOnceAsync();

        Assert.True(activity);
        Assert.Empty(notifier.Notifications);
        // The remote carries both, linearly: the desktop commit untouched, the worker's on top.
        Assert.Equal(Git(_watcher, "rev-parse", "HEAD"), Git(_remote, "rev-parse", "main"));
        Assert.Equal(desktopHead, Git(_remote, "rev-parse", "main~1"));
        Assert.Equal("worker commit", Git(_remote, "log", "-1", "--format=%s", "main"));
        Assert.Equal("", Git(_remote, "rev-list", "--merges", "main"));
    }

    [Fact]
    public async Task ConflictingCloneIsAbortedCleanlyAndNothingIsDispatched()
    {
        Commit(_watcher, "shared.md", "worker's line\n", "worker edit");
        Commit(_desktop, "shared.md", "desktop's line\n", "desktop edit");
        Git(_desktop, "push", "--quiet");
        Directory.CreateDirectory(Path.Combine(_watcher, "docs", "prompts"));
        File.WriteAllText(Path.Combine(_watcher, "docs", "prompts", "0001-cmd-hello.md"), TempProject.CmdMarkdown("ready"));
        Git(_watcher, "add", "-A");
        Git(_watcher, "commit", "--quiet", "-m", "lane"); // a ready command the poll must not dispatch
        var before = Git(_watcher, "rev-parse", "HEAD");
        var notifier = new FakeNotifier();
        var workers = new FakeProcessRunner();
        var log = new List<string>();
        var loop = Loop(notifier, workers, log);

        await loop.PollOnceAsync();
        await loop.PollOnceAsync();

        Assert.Equal(before, Git(_watcher, "rev-parse", "HEAD"));
        Assert.Equal("", Git(_watcher, "status", "--porcelain"));
        Assert.False(Directory.Exists(Path.Combine(_watcher, ".git", "rebase-merge")));
        Assert.False(Directory.Exists(Path.Combine(_watcher, ".git", "rebase-apply")));
        Assert.Empty(workers.ExecutableCalls);
        var notification = Assert.Single(notifier.Notifications);
        Assert.Equal("mf-watch: git pull conflict", notification.Title);
        Assert.Contains("shared.md", notification.Message);
        Assert.Contains(log, l => l.Contains("conflict in shared.md"));
    }
}
