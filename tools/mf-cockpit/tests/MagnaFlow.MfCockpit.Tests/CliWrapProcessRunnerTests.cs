using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// docs/prompts/0001-cmd-cockpit-spawn-errors.md: "No endpoint may 500 on a spawn failure" — the
/// runner itself must turn an OS-level start failure into a normal failed ProcessResult, not an
/// exception, so every caller's existing non-zero-exit handling already covers it.
/// </summary>
public class CliWrapProcessRunnerTests
{
    [Fact]
    public async Task Nonexistent_executable_returns_a_failed_result_instead_of_throwing()
    {
        var log = new FakeCockpitLog();
        var runner = new CliWrapProcessRunner(log);

        var result = await runner.RunExecutableAsync(
            "this-executable-does-not-exist-anywhere.exe", [], Path.GetTempPath());

        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("could not start", result.StdErr);
        Assert.Contains("this-executable-does-not-exist-anywhere.exe", result.StdErr);
        Assert.Single(log.Messages, m => m.Contains("spawn failed"));
    }
}
