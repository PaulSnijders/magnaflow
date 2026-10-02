using MagnaFlow.MfWatch.Prompts;

namespace MagnaFlow.MfWatch.Tests;

public class PromptStatusScannerTests
{
    [Fact]
    public void ReturnsEmptyWhenPromptsDirMissing()
    {
        using var project = new TempProject();
        Assert.Empty(PromptStatusScanner.Scan(project.Root));
    }

    [Fact]
    public void FindsReadyCommands()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "ready", title: "Say hello");

        var found = PromptStatusScanner.Scan(project.Root);

        var cmd = Assert.Single(found);
        Assert.Equal("0001-hello", cmd.Id);
        Assert.Equal("ready", cmd.Status);
        Assert.Equal("Say hello", cmd.Title);
        Assert.True(cmd.IsReady);
        Assert.False(cmd.IsRunning);
    }

    [Fact]
    public void OrdersById()
    {
        using var project = new TempProject();
        project.WriteCmd("0002-second");
        project.WriteCmd("0001-first");

        var found = PromptStatusScanner.Scan(project.Root);

        Assert.Equal(["0001-first", "0002-second"], found.Select(c => c.Id));
    }

    [Fact]
    public void FindsFollowUpSuffixedCommands_SortedBetweenParentAndNextNumber()
    {
        using var project = new TempProject();
        project.WriteCmd("0006-next", status: "ready");
        project.WriteCmd("0005-fix-lava", status: "done");
        project.WriteCmd("0005B-round2", status: "ready", title: "Round 2");

        var found = PromptStatusScanner.Scan(project.Root);

        Assert.Equal(["0005-fix-lava", "0005B-round2", "0006-next"], found.Select(c => c.Id));
        var followUp = found.Single(c => c.Id == "0005B-round2");
        Assert.Equal("ready", followUp.Status);
        Assert.Equal("Round 2", followUp.Title);
        Assert.True(followUp.IsReady);
    }

    [Fact]
    public void IgnoresNonCmdSiblingFiles()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello");
        var promptsDir = Path.Combine(project.Root, "docs", "prompts");
        File.WriteAllText(Path.Combine(promptsDir, "0001-pln-hello.md"), "# plan");
        File.WriteAllText(Path.Combine(promptsDir, "0001-qa-hello.md"), "# questions");
        File.WriteAllText(Path.Combine(promptsDir, "0001-rst-hello.md"), "# report");
        File.WriteAllText(Path.Combine(promptsDir, "README.md"), "not a command");

        var found = PromptStatusScanner.Scan(project.Root);

        Assert.Equal(["0001-hello"], found.Select(c => c.Id));
    }

    [Fact]
    public void SkipsMalformedFilesAndWarns()
    {
        using var project = new TempProject();
        var promptsDir = Path.Combine(project.Root, "docs", "prompts");
        Directory.CreateDirectory(promptsDir);
        File.WriteAllText(Path.Combine(promptsDir, "0001-cmd-broken.md"), "no frontmatter here");
        project.WriteCmd("0002-ok", status: "ready");

        var warnings = new List<string>();
        var found = PromptStatusScanner.Scan(project.Root, warnings.Add);

        Assert.Equal(["0002-ok"], found.Select(c => c.Id));
        Assert.Single(warnings);
        Assert.Contains("0001-broken", warnings[0]);
    }

    [Fact]
    public void ReadStatusRereadsAfterMutation()
    {
        using var project = new TempProject();
        var path = project.WriteCmd("0001-hello", status: "ready");
        Assert.Equal("ready", PromptStatusScanner.ReadStatus(path));

        File.WriteAllText(path, TempProject.CmdMarkdown(status: "done"));
        Assert.Equal("done", PromptStatusScanner.ReadStatus(path));
    }

    [Theory]
    [InlineData("Ready")]
    [InlineData("  ready  ")]
    [InlineData("READY")]
    public void StatusIsNormalized(string raw)
    {
        using var project = new TempProject();
        var path = project.WriteCmd("0001-hello", status: raw);
        var found = Assert.Single(PromptStatusScanner.Scan(project.Root));
        Assert.Equal("ready", found.Status);
        Assert.True(found.IsReady);
    }
}
