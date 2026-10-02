using MagnaFlow.WorkerController.Commands;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class StatusOverviewTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void BuildRows_ReflectsFrontmatterInIdOrder()
    {
        _project.WriteCmd("0002-b", TempProject.CmdMarkdown(title: "Command B", status: "done", branch: "task/0002-b", attempts: 1));
        _project.WriteCmd("0001-a", TempProject.CmdMarkdown(title: "Command A", status: "ready", branch: "task/0001-a"));

        var rows = StatusOverview.BuildRows(PromptScanner.Scan(_project.Root), configMaxAttempts: 3);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new StatusRow("0001-a", "Command A", "ready", "0/3", false), rows[0]);
        Assert.Equal(new StatusRow("0002-b", "Command B", "done", "1/3", false), rows[1]);
    }

    [Fact]
    public void BuildRows_MaxAttemptsFallsBackFrontmatterThenConfigThenDash()
    {
        _project.WriteCmd("0001-own", TempProject.CmdMarkdown(branch: "task/0001-own", maxAttempts: 5));
        _project.WriteCmd("0002-config", TempProject.CmdMarkdown(branch: "task/0002-config", maxAttempts: null));
        var commands = PromptScanner.Scan(_project.Root);

        var withConfig = StatusOverview.BuildRows(commands, configMaxAttempts: 4);
        Assert.Equal("0/5", withConfig[0].Attempts); // frontmatter wins
        Assert.Equal("0/4", withConfig[1].Attempts); // config default

        var withoutConfig = StatusOverview.BuildRows(commands, configMaxAttempts: null);
        Assert.Equal("0/-", withoutConfig[1].Attempts); // status works without config
    }

    [Fact]
    public void BuildRows_MarksMalformedCommandsWithWarningInsteadOfCrashing()
    {
        _project.WriteCmd("0001-broken", "no frontmatter");

        var rows = StatusOverview.BuildRows(PromptScanner.Scan(_project.Root), null);

        var row = Assert.Single(rows);
        Assert.True(row.Warning);
        Assert.Contains("malformed", row.Title);
    }

    [Fact]
    public void BuildRows_DoesNotModifyAnyFile()
    {
        var path = _project.WriteCmd("0001-a", TempProject.CmdMarkdown(branch: "task/0001-a"));
        var before = File.GetLastWriteTimeUtc(path);

        StatusOverview.BuildRows(PromptScanner.Scan(_project.Root), 3);

        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    public void Dispose() => _project.Dispose();
}
