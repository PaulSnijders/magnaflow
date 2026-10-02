using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class PlnFileTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void Write_ThenReadOpenQuestions_RoundTrips()
    {
        var path = _project.WritePln("0001-test", "placeholder");
        PlnFile.Write(path, "Do the thing.",
            selfAnswered: [("What color?", "Blue, per the design spec.")],
            openQuestions: ["Should this ship behind a flag?", "Who approves the copy?"]);

        var open = PlnFile.ReadOpenQuestions(path);

        Assert.Equal(["Should this ship behind a flag?", "Who approves the copy?"], open);
        var text = File.ReadAllText(path);
        Assert.Contains("# Plan", text);
        Assert.Contains("Do the thing.", text);
        Assert.Contains("## Self-answered questions", text);
        Assert.Contains("Q: What color? → A: Blue, per the design spec.", text);
    }

    [Fact]
    public void ReadOpenQuestions_ReturnsEmptyWhenSectionAbsent()
    {
        var path = _project.WritePln("0001-test", "placeholder");
        PlnFile.Write(path, "Do the thing.");

        Assert.Empty(PlnFile.ReadOpenQuestions(path));
    }

    [Fact]
    public void ReadOpenQuestions_ReturnsEmptyWhenFileMissing() =>
        Assert.Empty(PlnFile.ReadOpenQuestions(Path.Combine(_project.Root, "docs", "prompts", "0001-pln-nope.md")));

    [Fact]
    public void Write_UpdatesInPlace_NotAppendingHistory()
    {
        var path = _project.WritePln("0001-test", "placeholder");
        PlnFile.Write(path, "First plan.", openQuestions: ["Q1?"]);
        PlnFile.Write(path, "Re-plan after the answer.", openQuestions: []);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("First plan.", text);
        Assert.Contains("Re-plan after the answer.", text);
        Assert.Empty(PlnFile.ReadOpenQuestions(path));
    }

    public void Dispose() => _project.Dispose();
}
