using MagnaFlow.WorkerController.Infrastructure;

namespace MagnaFlow.WorkerController.Tests;

/// <summary>
/// StageIfPresentAsync against the real git CLI (no fakes): the whole point of it is which exit
/// codes git actually produces for a missing and for an ignored pathspec, so faking git here
/// would only test the assumption instead of the behaviour.
/// </summary>
public class GitClientTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly GitClient _git;

    public GitClientTests()
    {
        _git = new GitClient(new CliWrapProcessRunner(), _project.Root);
        Run("init", "--quiet", "--initial-branch=main");
        Run("config", "user.email", "test@example.com");
        Run("config", "user.name", "mf-tests");
    }

    private void Run(params string[] args)
    {
        var result = new CliWrapProcessRunner()
            .RunExecutableAsync("git", args, _project.Root, timeout: TimeSpan.FromSeconds(30))
            .GetAwaiter().GetResult();
        Assert.True(result.Succeeded, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private string StagedPaths()
    {
        var result = new CliWrapProcessRunner()
            .RunExecutableAsync("git", ["diff", "--cached", "--name-only"], _project.Root, timeout: TimeSpan.FromSeconds(30))
            .GetAwaiter().GetResult();
        return result.StdOut;
    }

    [Fact]
    public async Task StageIfPresentAsync_stages_a_tracked_evidence_folder_normally()
    {
        _project.WriteSessionEvidence("0001-test", "sess-1");

        await _git.StageIfPresentAsync(".magnaflow/0001-test");

        Assert.Contains(".magnaflow/0001-test/session.yml", StagedPaths());
    }

    [Fact]
    public async Task StageIfPresentAsync_skips_a_folder_that_was_never_written()
    {
        // wozzol2's 0047/0048: no session id came back, so SessionEvidence wrote nothing.
        await _git.StageIfPresentAsync(".magnaflow/0048-never-written");

        Assert.Equal("", StagedPaths().Trim());
    }

    [Fact]
    public async Task StageIfPresentAsync_skips_a_folder_the_project_ignores()
    {
        // wozzol2 since ad9f725: `.magnaflow/*` with a single `!.magnaflow/config.yml` exception.
        _project.WriteFile(".gitignore", ".magnaflow/*\n!.magnaflow/config.yml\n");
        _project.WriteSessionEvidence("0001-test", "sess-1");

        await _git.StageIfPresentAsync(".magnaflow/0001-test");

        Assert.Equal("", StagedPaths().Trim());
    }

    [Fact]
    public async Task StagePathAsync_still_throws_on_the_same_paths()
    {
        _project.WriteFile(".gitignore", ".magnaflow/*\n");
        _project.WriteSessionEvidence("0001-test", "sess-1");

        await Assert.ThrowsAsync<GitException>(() => _git.StagePathAsync(".magnaflow/0001-test"));
        await Assert.ThrowsAsync<GitException>(() => _git.StagePathAsync(".magnaflow/0048-never-written"));
    }

    // --- GetTouchedPathsAsync (prompts/0037): what a path-scoped check sees as "touched" ---

    /// <summary>The async twin of Run, for use inside async tests.</summary>
    private async Task GitAsync(params string[] args)
    {
        var result = await new CliWrapProcessRunner()
            .RunExecutableAsync("git", args, _project.Root, timeout: TimeSpan.FromSeconds(30));
        Assert.True(result.Succeeded, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private async Task<string> CommitAllAsync(string message)
    {
        await GitAsync("add", "-A");
        await GitAsync("commit", "--quiet", "-m", message);
        return await _git.GetHeadCommitAsync();
    }

    [Fact]
    public async Task GetTouchedPathsAsync_covers_commits_uncommitted_and_untracked_files()
    {
        _project.WriteFile("klaas-backend/a.cs", "a");
        _project.WriteFile("topo/b.ts", "b");
        _project.WriteFile("staged.txt", "s");
        var start = await CommitAllAsync("start");

        _project.WriteFile("klaas-backend/a.cs", "a2");             // committed during the run
        await CommitAllAsync("agent committed");
        _project.WriteFile("topo/b.ts", "b2");                      // uncommitted, unstaged
        _project.WriteFile("staged.txt", "s2");
        await GitAsync("add", "staged.txt");                                   // uncommitted, staged
        _project.WriteFile("topo/new.ts", "n");                     // untracked

        var touched = await _git.GetTouchedPathsAsync(start);

        Assert.Equal(["klaas-backend/a.cs", "staged.txt", "topo/b.ts", "topo/new.ts"], touched);
    }

    [Fact]
    public async Task GetTouchedPathsAsync_counts_a_rename_as_both_paths()
    {
        _project.WriteFile("topo/old.ts", "same content, long enough to be detected as a rename\n");
        var start = await CommitAllAsync("start");
        Directory.CreateDirectory(Path.Combine(_project.Root, "klaas-ionic"));
        await GitAsync("mv", "topo/old.ts", "klaas-ionic/new.ts");
        await CommitAllAsync("renamed");

        var touched = await _git.GetTouchedPathsAsync(start);

        Assert.Equal(["klaas-ionic/new.ts", "topo/old.ts"], touched);
    }

    [Fact]
    public async Task GetTouchedPathsAsync_leaves_ignored_files_out()
    {
        _project.WriteFile(".gitignore", "bin/\n");
        var start = await CommitAllAsync("start");
        _project.WriteFile("bin/out.dll", "x");
        _project.WriteFile("src/a.cs", "a");

        var touched = await _git.GetTouchedPathsAsync(start);

        Assert.Equal(["src/a.cs"], touched);
    }

    [Fact]
    public async Task Lane_files_and_the_evidence_folder_do_not_count()
    {
        var cmdPath = _project.WriteCmd("0001-test", TempProject.CmdMarkdown());
        var start = await CommitAllAsync("claim");
        _project.WriteCmd("0001-test", TempProject.CmdMarkdown(status: "running"));
        _project.WritePln("0001-test", "plan");
        _project.WriteRst("0001-test", "report");
        _project.WriteFile(".magnaflow/0001-test/build.log", "log");
        _project.WriteCmd("0002-other", TempProject.CmdMarkdown());  // another cmd's file is a real change
        _project.WriteFile("topo/a.ts", "a");
        var (cmd, _) = MagnaFlow.WorkerController.Prompts.CmdFile.Parse(cmdPath, "0001-test");

        var touched = MagnaFlow.WorkerController.Execution.TouchedPaths.From(
            await _git.GetTouchedPathsAsync(start), cmd!, _project.Root);

        Assert.Equal(["docs/prompts/0002-cmd-other.md", "topo/a.ts"], touched.Paths);
    }

    [Fact]
    public async Task A_resumed_run_on_a_reused_branch_counts_the_earlier_runs_work()
    {
        _project.WriteFile("README.md", "r");
        await CommitAllAsync("base");
        await GitAsync("checkout", "--quiet", "-b", "task/0001-test");
        _project.WriteFile("topo/earlier.ts", "from the first run");
        await CommitAllAsync("command 0001-test: first run");
        await GitAsync("checkout", "--quiet", "main");
        _project.WriteCmd("0001-test", TempProject.CmdMarkdown(status: "running"));
        var claim = await CommitAllAsync("mf-worker: 0001-test -> running");  // the resumed run's start point
        await GitAsync("checkout", "--quiet", "task/0001-test");
        _project.WriteFile("klaas-backend/now.cs", "this run");

        var touched = await _git.GetTouchedPathsAsync(claim);

        Assert.Contains("topo/earlier.ts", touched);
        Assert.Contains("klaas-backend/now.cs", touched);
    }

    public void Dispose() => _project.Dispose();
}
