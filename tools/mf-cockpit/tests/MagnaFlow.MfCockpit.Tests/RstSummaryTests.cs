using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Tests;

public class RstSummaryTests
{
    [Fact]
    public void Read_returns_the_summary_from_the_frontmatter()
    {
        using var project = new TempProject();
        var path = project.WriteSibling("0001-hello", "rst", """
            ---
            title: Say hello
            cmd: 0001-cmd-hello.md
            done: 2026-08-17
            summary: The greeting is now rendered server-side; nothing to do.
            ---

            ## What was done

            A very long report written for the next session to read.
            """);

        Assert.Equal("The greeting is now rendered server-side; nothing to do.", RstSummary.Read(path));
    }

    [Fact]
    public void Read_unquotes_a_summary_that_contains_a_colon()
    {
        using var project = new TempProject();
        var path = project.WriteSibling("0001-hello", "rst", """
            ---
            summary: "Fixed: the lane no longer collapses on refresh."
            ---

            ## What was done
            """);

        Assert.Equal("Fixed: the lane no longer collapses on refresh.", RstSummary.Read(path));
    }

    [Fact]
    public void Read_is_null_when_the_frontmatter_has_no_summary()
    {
        using var project = new TempProject();
        var path = project.WriteSibling("0001-hello", "rst", """
            ---
            title: Say hello
            cmd: 0001-cmd-hello.md
            done: 2026-07-13
            ---

            ## What was done
            """);

        Assert.Null(RstSummary.Read(path));
    }

    [Fact]
    public void Read_is_null_when_there_is_no_frontmatter_at_all()
    {
        using var project = new TempProject();
        var path = project.WriteSibling("0001-hello", "rst", "## What was done\n\nsummary: not in frontmatter.\n");

        Assert.Null(RstSummary.Read(path));
    }

    [Fact]
    public void Read_is_null_when_the_frontmatter_is_malformed()
    {
        using var project = new TempProject();
        var unclosedFence = project.WriteSibling("0001-hello", "rst", "---\nsummary: never closed\n\n## What was done\n");
        var invalidYaml = project.WriteSibling("0002-broken", "rst", "---\nsummary: [unterminated\ntitle: x\n---\n\nbody\n");

        Assert.Null(RstSummary.Read(unclosedFence));
        Assert.Null(RstSummary.Read(invalidYaml));
    }

    [Fact]
    public void Read_is_null_for_a_missing_file_or_a_null_path()
    {
        using var project = new TempProject();

        Assert.Null(RstSummary.Read(null));
        Assert.Null(RstSummary.Read(Path.Combine(project.PromptsDir, "0001-rst-nope.md")));
    }

    [Fact]
    public void Read_never_reads_past_the_frontmatter()
    {
        using var project = new TempProject();
        // A report body is written for an AI reader and can be arbitrarily long; a stray "summary:"
        // line down there is body text, not the field.
        var body = string.Join("\n", Enumerable.Repeat("summary: not this one", 5_000));
        var path = project.WriteSibling("0001-hello", "rst", $"---\nsummary: this one\n---\n\n{body}\n");

        Assert.Equal("this one", RstSummary.Read(path));
    }
}
