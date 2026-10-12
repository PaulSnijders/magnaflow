using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>GitClient.GetHeadShaAsync — the HEAD the stable row's Behind compares with
/// (docs/specs/cockpit/project.md#run): exactly one `git rev-parse HEAD`, and any failure is null.</summary>
public class GitClientHeadTests
{
    [Fact]
    public async Task Head_is_one_rev_parse_trimmed()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(0, "0123abc\n", "", false));

        var head = await new GitClient(runner).GetHeadShaAsync("/repo");

        Assert.Equal("0123abc", head);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("git", call.Executable);
        Assert.Equal(["--no-pager", "rev-parse", "HEAD"], call.Arguments);
    }

    [Theory]
    [InlineData(128, "", false)]   // not a repo / no commits yet
    [InlineData(0, "", true)]      // timed out
    [InlineData(0, "  \n", false)] // nothing usable
    public async Task A_failed_rev_parse_is_null(int exitCode, string stdout, bool timedOut)
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(exitCode, stdout, "fatal: not a git repository", timedOut));

        Assert.Null(await new GitClient(runner).GetHeadShaAsync("/repo"));
    }

    [Fact]
    public async Task A_spawn_that_throws_is_null()
    {
        var runner = new FakeProcessRunner { OnRun = (_, _, _, _) => throw new InvalidOperationException("git not found") };

        Assert.Null(await new GitClient(runner).GetHeadShaAsync("/repo"));
    }
}
