using MagnaFlow.WorkerController.Commands;
using Spectre.Console;

namespace MagnaFlow.WorkerController.Tests;

public class CliTests
{
    [Theory]
    [InlineData("run", "--bogus", "0001-x")]
    [InlineData("run", "0001-x", "extra")]
    [InlineData("run")]
    [InlineData("frobnicate")]
    [InlineData("next", "--force")]
    [InlineData("run-all", "stray")]
    public async Task UsageErrors_ExitTwoWithTheMessageOnStderr(params string[] args)
    {
        var stderr = new StringWriter();

        var exit = await WorkerCli.RunAsync(args, stderr, QuietConsole(new StringWriter()));

        Assert.Equal(ExitCodes.UsageError, exit);
        Assert.StartsWith("mf-worker: ", stderr.ToString());
        Assert.Contains("usage: mf-worker run <cmd-id>", stderr.ToString());
    }

    [Fact]
    public async Task Help_ExitsZeroAndDescribesCommandsInLaneTerms()
    {
        var stdout = new StringWriter();

        var exit = await WorkerCli.RunAsync(["--help"], new StringWriter(), QuietConsole(stdout));

        Assert.Equal(ExitCodes.Success, exit);
        var help = stdout.ToString();
        Assert.Contains("first ready command in ID order", help);
        Assert.DoesNotContain("pending task", help);
        Assert.DoesNotContain("folder-name", help);
    }

    private static IAnsiConsole QuietConsole(TextWriter writer) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(writer),
    });
}
