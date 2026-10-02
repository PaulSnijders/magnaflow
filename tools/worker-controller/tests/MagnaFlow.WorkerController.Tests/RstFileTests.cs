using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class RstFileTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void Exists_IsFalseUntilWritten()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-rst-test.md");

        Assert.False(RstFile.Exists(path));

        RstFile.Write(path, "Added the footer component.");

        Assert.True(RstFile.Exists(path));
    }

    [Fact]
    public void Write_IncludesDeviationsAndNeverRestatesThePlan()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-rst-test.md");

        RstFile.Write(path, "Added the footer.", decisions: "Used the existing .btn-secondary style instead of a new variant.");

        var text = File.ReadAllText(path);
        Assert.Contains("## What was done", text);
        Assert.Contains("Added the footer.", text);
        Assert.Contains("## Decisions and deviations from the plan", text);
        Assert.Contains(".btn-secondary", text);
        Assert.DoesNotContain("## Abort reason", text);
    }

    [Fact]
    public void Write_IncludesAbortReasonWhenGiven()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-rst-test.md");

        RstFile.Write(path, "Attempted the change.", abortReason: "build kept failing after 3 attempt(s)");

        var text = File.ReadAllText(path);
        Assert.Contains("## Abort reason", text);
        Assert.Contains("build kept failing", text);
    }

    [Fact]
    public void WriteFallback_ProducesAMinimalReportWhenTheAgentWroteNone()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-rst-test.md");

        RstFile.WriteFallback(path, succeeded: false, attempts: 3, abortReason: "retries exhausted");

        var text = File.ReadAllText(path);
        Assert.Contains("aborted after 3 attempt(s)", text);
        Assert.Contains("retries exhausted", text);
    }

    [Fact]
    public void Write_OpensWithTheSameFrontmatterAHandWrittenReportHas()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0007-rst-add-export-button.md");

        RstFile.Write(
            path,
            "Added the export button.",
            title: "Add an export button",
            summary: "The orders page has an export button; nothing to do.",
            done: new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));

        var lines = File.ReadAllLines(path);
        Assert.Equal("---", lines[0]);
        Assert.Equal("title: Add an export button", lines[1]);
        Assert.Equal("cmd: 0007-cmd-add-export-button.md", lines[2]);
        Assert.Equal("done: 2026-08-17", lines[3]);
        Assert.Equal("summary: The orders page has an export button; nothing to do.", lines[4]);
        Assert.Equal("---", lines[5]);
        Assert.Contains("## What was done", File.ReadAllText(path));
    }

    [Fact]
    public void Write_QuotesAFrontmatterValueThatWouldOtherwiseBreakTheYaml()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-rst-test.md");

        RstFile.Write(path, "Done.", title: "mf-run: robust status", summary: "Fixed: the status is now explainable.");

        var text = File.ReadAllText(path);
        Assert.Contains("title: \"mf-run: robust status\"", text);
        Assert.Contains("summary: \"Fixed: the status is now explainable.\"", text);
    }

    [Fact]
    public void Write_FallsBackToTheFileNameAndTheFirstLineWhenNoTitleOrSummaryIsGiven()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0002-rst-add-footer.md");

        RstFile.Write(path, "Added the footer.\n\nMore detail that is not the summary.");

        var text = File.ReadAllText(path);
        Assert.Contains("title: add footer", text);
        // Only the first line — the summary is one line by construction, never the whole report.
        Assert.Equal("summary: Added the footer.", SummaryLine(text));
    }

    [Fact]
    public void WriteFallback_SummarizesItselfAsTheAgentHavingWrittenNoReport()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-rst-test.md");

        RstFile.WriteFallback(path, succeeded: false, attempts: 3, abortReason: "retries exhausted", title: "Do the thing");

        var text = File.ReadAllText(path);
        Assert.Contains("title: Do the thing", text);
        Assert.Contains("cmd: 0001-cmd-test.md", text);
        Assert.Contains("Aborted after 3 attempt(s)", SummaryLine(text));
    }

    [Fact]
    public void WriteFallback_SummaryIsReadableForASucceededRunToo()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-rst-test.md");

        RstFile.WriteFallback(path, succeeded: true, attempts: 1, abortReason: null);

        Assert.Contains("Completed after 1 attempt(s)", SummaryLine(File.ReadAllText(path)));
    }

    private static string SummaryLine(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).First(l => l.StartsWith("summary:", StringComparison.Ordinal));

    public void Dispose() => _project.Dispose();
}
