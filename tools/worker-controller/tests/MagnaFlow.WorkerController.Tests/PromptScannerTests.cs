using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class PromptScannerTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void Scan_ReturnsCommandsInIdOrder()
    {
        _project.WriteCmd("0003-c", TempProject.CmdMarkdown(branch: "task/0003-c"));
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a"));
        _project.WriteCmd("0002-b", TempProject.CmdMarkdown(branch: "task/0002-b"));

        var commands = PromptScanner.Scan(_project.Root);

        Assert.Equal(["0001-a", "0002-b", "0003-c"], commands.Select(c => c.Id));
        Assert.All(commands, c => Assert.False(c.IsMalformed));
    }

    [Fact]
    public void Scan_ReportsInvalidYamlAsMalformedWithoutCrashing()
    {
        _project.WriteCmd("0001-broken", "---\ntitle: [unclosed\n---\nbody");
        _project.WriteCmd("0002-good", TempProject.CmdMarkdown(branch: "task/0002-good"));

        var commands = PromptScanner.Scan(_project.Root);

        Assert.Equal(2, commands.Count);
        Assert.True(commands[0].IsMalformed);
        Assert.False(commands[1].IsMalformed);
    }

    [Fact]
    public void Scan_ReturnsEmptyWhenPromptsFolderAbsent() =>
        Assert.Empty(PromptScanner.Scan(_project.Root));

    [Fact]
    public void Scan_IgnoresFilesThatDoNotMatchTheCmdPattern()
    {
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a"));
        _project.WriteFile("docs/prompts/README.md", "not a command");

        var commands = PromptScanner.Scan(_project.Root);

        Assert.Single(commands);
        Assert.Equal("0001-a", commands[0].Id);
    }

    [Fact]
    public void Scan_ReportsOrphanedSiblingFilesAsMalformed()
    {
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a"));
        _project.WritePln("0002-orphan", "# Plan\n\nNo matching cmd file.");

        var commands = PromptScanner.Scan(_project.Root);

        Assert.Equal(2, commands.Count);
        var orphan = Assert.Single(commands, c => c.Id == "0002-orphan");
        Assert.True(orphan.IsMalformed);
        Assert.Contains("orphaned sibling", orphan.Error);
    }

    [Fact]
    public void Scan_AcceptsFollowUpSuffixedIds_SortedBetweenParentAndNextNumber()
    {
        _project.WriteCmd("0006-next", TempProject.CmdMarkdown(branch: "task/0006-next"));
        _project.WriteCmd("0005-fix-lava", TempProject.CmdMarkdown(branch: "task/0005-fix-lava"));
        _project.WriteCmd("0005B-round2", TempProject.CmdMarkdown(branch: "task/0005B-round2"));
        _project.WriteCmd("0005C-round3", TempProject.CmdMarkdown(branch: "task/0005C-round3"));

        var commands = PromptScanner.Scan(_project.Root);

        Assert.Equal(["0005-fix-lava", "0005B-round2", "0005C-round3", "0006-next"], commands.Select(c => c.Id));
        Assert.All(commands, c => Assert.False(c.IsMalformed));
    }

    [Fact]
    public void FindById_LocatesTheMatchingCommand()
    {
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a"));

        var found = PromptScanner.FindById(PromptScanner.Scan(_project.Root), "0001-a");

        Assert.NotNull(found);
        Assert.Equal("0001-a", found.Id);
    }

    public void Dispose() => _project.Dispose();
}
