using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class NextSelectionTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void FirstReady_PicksLowestNumberedReadyCommand()
    {
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(status: "done", branch: "task/0001-a"));
        _project.WriteCmd("0002-b", TempProject.CmdMarkdown(status: "ready", branch: "task/0002-b"));
        _project.WriteCmd("0003-c", TempProject.CmdMarkdown(status: "ready", branch: "task/0003-c"));

        var next = PromptScanner.FirstReady(PromptScanner.Scan(_project.Root));

        Assert.Equal("0002-b", next?.Id);
    }

    [Fact]
    public void FirstReady_SkipsNonReadyAndMalformedCommands()
    {
        _project.WriteCmd("0001-broken", "no frontmatter");
        _project.WriteCmd("0002-busy", TempProject.CmdMarkdown(status: "running", branch: "task/0002-busy"));
        _project.WriteCmd("0003-aborted", TempProject.CmdMarkdown(status: "aborted", branch: "task/0003-aborted"));
        _project.WriteCmd("0004-questions", TempProject.CmdMarkdown(status: "questions", branch: "task/0004-questions"));
        _project.WriteCmd("0005-open", TempProject.CmdMarkdown(status: "ready", branch: "task/0005-open"));

        var next = PromptScanner.FirstReady(PromptScanner.Scan(_project.Root));

        Assert.Equal("0005-open", next?.Id);
    }

    [Fact]
    public void FirstReady_ReturnsNullWhenNothingReady()
    {
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(status: "done", branch: "task/0001-a"));

        Assert.Null(PromptScanner.FirstReady(PromptScanner.Scan(_project.Root)));
    }

    [Fact]
    public void FirstReady_ReturnsNullOnEmptyQueue() =>
        Assert.Null(PromptScanner.FirstReady(PromptScanner.Scan(_project.Root)));

    public void Dispose() => _project.Dispose();
}
