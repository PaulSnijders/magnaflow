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

    private void WriteCmd(string id)
    {
        Directory.CreateDirectory(Path.Combine(_watcher, "docs", "prompts"));
        File.WriteAllText(Path.Combine(_watcher, "docs", "prompts", $"{id[..4]}-cmd-{id[5..]}.md"), TempProject.CmdMarkdown("ready"));
    }

    [Fact]
    public async Task EachCommandReachesTheRemoteBeforeTheNextOneRuns()
    {
        WriteCmd("0001-a");
        WriteCmd("0002-b");
        Git(_watcher, "add", "-A");
        Git(_watcher, "commit", "--quiet", "-m", "lane");
        var remoteSubjectsSeen = new List<string>();
        var workers = new FakeProcessRunner
        {
            OnRunExecutable = (_, args) =>
            {
                var id = args[3];
                remoteSubjectsSeen.Add(Git(_remote, "log", "-1", "--format=%s", "main"));
                File.WriteAllText(Path.Combine(_watcher, "docs", "prompts", $"{id[..4]}-cmd-{id[5..]}.md"), TempProject.CmdMarkdown("done"));
                Git(_watcher, "add", "-A");
                Git(_watcher, "commit", "--quiet", "-m", $"mf-worker: {id} -> done");
                return new ProcessResult(0, "", "", false);
            },
        };

        await Loop(new FakeNotifier(), workers).PollOnceAsync();

        // The local lane commit leaves with the first push; 0001's commit is out before 0002 starts.
        Assert.Equal(["seed", "mf-worker: 0001-a -> done"], remoteSubjectsSeen);
        Assert.Equal("mf-worker: 0002-b -> done", Git(_remote, "log", "-1", "--format=%s", "main"));
    }

    [Fact]
    public async Task ConflictIsResolvedByTheOnConflictActionAndTheCommandRuns()
    {
        Commit(_watcher, "shared.md", "worker's line\n", "worker edit");
        Commit(_desktop, "shared.md", "desktop's line\n", "desktop edit");
        Directory.CreateDirectory(Path.Combine(_desktop, "docs", "prompts"));
        File.WriteAllText(Path.Combine(_desktop, "docs", "prompts", "0001-cmd-hello.md"), TempProject.CmdMarkdown("ready"));
        Git(_desktop, "add", "-A");
        Git(_desktop, "commit", "--quiet", "-m", "lane");
        Git(_desktop, "push", "--quiet");
        // The action file, kept out of git here so it plays no part in the conflict.
        Directory.CreateDirectory(Path.Combine(_watcher, ".magnaflow"));
        File.WriteAllText(Path.Combine(_watcher, ".magnaflow", "actions.yml"), "actions:\n  resolve-conflicts:\n    prompt: resolve it\n");
        File.WriteAllText(Path.Combine(_watcher, ".git", "info", "exclude"), ".magnaflow/\n");
        var workers = new FakeProcessRunner
        {
            OnRunExecutable = (_, args) =>
            {
                if (args[0] == "action")
                {
                    // The "agent": keep both sides by replaying the worker's edit on top by hand.
                    Git(_watcher, "fetch", "--quiet");
                    Git(_watcher, "reset", "--quiet", "--hard", "origin/main");
                    Commit(_watcher, "shared.md", "desktop's line\nworker's line\n", "worker edit (resolved)");
                    return new ProcessResult(0, "", "", false);
                }
                File.WriteAllText(Path.Combine(_watcher, "docs", "prompts", "0001-cmd-hello.md"), TempProject.CmdMarkdown("done"));
                Git(_watcher, "add", "docs");
                Git(_watcher, "commit", "--quiet", "-m", "mf-worker: 0001-hello -> done");
                return new ProcessResult(0, "", "", false);
            },
        };
        var notifier = new FakeNotifier();

        var activity = await Loop(notifier, workers).PollOnceAsync();

        Assert.True(activity);
        Assert.Equal(["action", "run"], workers.ExecutableCalls.Select(c => c.Arguments[0]));
        Assert.Equal("conflict", workers.ExecutableCalls[0].Arguments[5]);
        var notification = Assert.Single(notifier.Notifications, n => n.Title.StartsWith("mf-watch:"));
        Assert.Equal("mf-watch: git conflict resolved", notification.Title);
        Assert.Equal("mf-worker: 0001-hello -> done", Git(_remote, "log", "-1", "--format=%s", "main"));
        Assert.Equal("", Git(_remote, "rev-list", "--merges", "main"));
    }
}
