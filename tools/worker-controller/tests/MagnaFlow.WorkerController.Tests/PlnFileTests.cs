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

    /// <summary>The shape that split into three qa rounds in 0019: one question, nested options, a trailing recommendation.</summary>
    internal const string Shape0019 =
        """
        # Plan

        Do step 1 and step 2.

        ## Open questions (this round)

        - **How far should step 1 go, given that a terminal Ctrl+C reaches the
          worker directly through the process group?** Two options:
          - (A) Do only the prescribed token separation in this run. Keep a
            narrowed BUG line.
          - (B) Also detach the worker from the console in this run. On Unix,
            that means setsid.

        Recommendation: (A), because the watcher runs under systemd.
        """;

    [Fact]
    public void ReadOpenQuestions_OneMultiLineQuestionWithNestedOptions_IsOneQuestionWithAllItsText()
    {
        var path = _project.WritePln("0001-test", Shape0019);

        var open = PlnFile.ReadOpenQuestions(path);

        var question = Assert.Single(open);
        Assert.StartsWith("**How far should step 1 go", question);
        Assert.Contains("  worker directly through the process group?** Two options:", question);
        Assert.Contains("  - (A) Do only the prescribed token separation in this run. Keep a", question);
        Assert.Contains("    narrowed BUG line.", question);
        Assert.Contains("  - (B) Also detach the worker from the console in this run. On Unix,", question);
        Assert.Contains("    that means setsid.", question);
        Assert.EndsWith("Recommendation: (A), because the watcher runs under systemd.", question);
    }

    [Fact]
    public void ReadOpenQuestions_TwoOneLineQuestions_AreTwoQuestions()
    {
        var path = _project.WritePln("0001-test",
            "# Plan\n\nx\n\n## Open questions (this round)\n\n- First?\n- Second?\n");

        Assert.Equal(["First?", "Second?"], PlnFile.ReadOpenQuestions(path));
    }

    public void Dispose() => _project.Dispose();
}
