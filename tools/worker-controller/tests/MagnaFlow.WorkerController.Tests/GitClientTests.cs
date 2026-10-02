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

    public void Dispose() => _project.Dispose();
}
