using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class CmdFileTests : IDisposable
{
    private readonly TempProject _project = new();

    private const string FullCmd =
        """
        ---
        title: Set up project
        status: ready        # draft | ready | running | questions | done | aborted
        branch: task/0001-project-setup
        base: main
        group: 001-feature-x
        fresh_session: true
        specs:
          - docs/specs/example.md
          - docs/specs/second.md
        attempts: 0
        max_attempts: 5
        created: 2026-07-08
        ---

        ## Goal
        What must be finished.

        ## Acceptance criteria
        - [ ] Criterion 1
        """;

    [Fact]
    public void Parse_ReadsAllFrontmatterFields()
    {
        var path = _project.WriteCmd("0001-project-setup", FullCmd);
        var (cmd, error) = CmdFile.Parse(path, "0001-project-setup");

        Assert.Null(error);
        Assert.NotNull(cmd);
        Assert.Equal("0001-project-setup", cmd.Id);
        Assert.Equal("0001", cmd.Number);
        Assert.Equal("project-setup", cmd.Name);
        Assert.Equal("Set up project", cmd.Title);
        Assert.Equal(CmdStatus.Ready, cmd.Status);
        Assert.Equal("task/0001-project-setup", cmd.Branch);
        Assert.Equal("main", cmd.Base);
        Assert.Equal("001-feature-x", cmd.Group);
        Assert.True(cmd.FreshSession);
        Assert.Equal(["docs/specs/example.md", "docs/specs/second.md"], cmd.Specs);
        Assert.Equal(0, cmd.Attempts);
        Assert.Equal(5, cmd.MaxAttempts);
        Assert.Contains("## Goal", cmd.Body);
    }

    [Fact]
    public void Parse_AppliesDefaultsForOptionalFields()
    {
        var path = _project.WriteCmd("0002-bare", TempProject.CmdMarkdown(branch: "task/0002-bare", maxAttempts: null));
        var (cmd, error) = CmdFile.Parse(path, "0002-bare");

        Assert.Null(error);
        Assert.NotNull(cmd);
        Assert.Null(cmd.Base);
        Assert.Null(cmd.Group);
        Assert.False(cmd.FreshSession);
        Assert.Empty(cmd.Specs);
        Assert.Null(cmd.MaxAttempts);
    }

    [Fact]
    public void Parse_MissingTitle_FallsBackToNameSlug()
    {
        var content = "---\nstatus: ready\nattempts: 0\n---\nbody";
        var path = _project.WriteCmd("0011-add-export-button", content);
        var (cmd, error) = CmdFile.Parse(path, "0011-add-export-button");

        Assert.Null(error);
        Assert.Equal("add export button", cmd!.Title);
    }

    [Theory]
    [InlineData("status: reviewing", "unknown 'status'")]
    [InlineData("", "missing")]
    public void Parse_RejectsMissingOrUnknownStatus(string statusLine, string expectedError)
    {
        var content = $"---\ntitle: X\n{statusLine}\nbranch: task/x\nattempts: 0\n---\nbody";
        var path = _project.WriteCmd("0003-invalid", content);
        var (cmd, error) = CmdFile.Parse(path, "0003-invalid");

        Assert.Null(cmd);
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void Parse_AllowsMissingBranch_BranchlessMode()
    {
        var path = _project.WriteCmd("0007-branchless", TempProject.CmdMarkdown(branch: null));
        var (cmd, error) = CmdFile.Parse(path, "0007-branchless");

        Assert.Null(error);
        Assert.Null(cmd!.Branch);
    }

    [Fact]
    public void Parse_RejectsBaseWithoutBranch()
    {
        var content = "---\ntitle: X\nstatus: ready\nbase: main\nattempts: 0\n---\nbody";
        var path = _project.WriteCmd("0008-invalid", content);
        var (cmd, error) = CmdFile.Parse(path, "0008-invalid");

        Assert.Null(cmd);
        Assert.Contains("'base' but no 'branch'", error);
    }

    [Fact]
    public void Parse_ReadsResumeField()
    {
        var path = _project.WriteCmd("0009-follow-up", TempProject.CmdMarkdown(branch: null, resume: "0001-earlier"));
        var (cmd, error) = CmdFile.Parse(path, "0009-follow-up");

        Assert.Null(error);
        Assert.Equal("0001-earlier", cmd!.Resume);
    }

    [Fact]
    public void Parse_RejectsResumeCombinedWithFreshSession()
    {
        var content = "---\ntitle: X\nstatus: ready\nresume: 0001-earlier\nfresh_session: true\nattempts: 0\n---\nbody";
        var path = _project.WriteCmd("0010-conflict", content);
        var (cmd, error) = CmdFile.Parse(path, "0010-conflict");

        Assert.Null(cmd);
        Assert.Contains("contradict", error);
    }

    [Fact]
    public void Parse_RejectsFileWithoutFrontmatter()
    {
        var path = _project.WriteCmd("0004-none", "# plain markdown\n");
        var (cmd, error) = CmdFile.Parse(path, "0004-none");

        Assert.Null(cmd);
        Assert.Contains("frontmatter", error);
    }

    [Fact]
    public void WriteStatus_ChangesOnlyTheStatusValue()
    {
        var path = _project.WriteCmd("0001-project-setup", FullCmd);
        var (cmd, _) = CmdFile.Parse(path, "0001-project-setup");

        cmd!.WriteStatus(CmdStatus.Running);

        var expected = FullCmd.Replace(
            "status: ready        # draft | ready | running | questions | done | aborted",
            "status: running        # draft | ready | running | questions | done | aborted");
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Fact]
    public void WriteAttempts_ChangesOnlyTheAttemptsValue()
    {
        var path = _project.WriteCmd("0001-project-setup", FullCmd);
        var (cmd, _) = CmdFile.Parse(path, "0001-project-setup");

        cmd!.WriteAttempts(2);

        var expected = FullCmd.Replace("attempts: 0", "attempts: 2");
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Fact]
    public void WriteAttempts_AppendsLineWhenAttemptsFieldMissing()
    {
        var content = "---\ntitle: X\nstatus: ready\nbranch: task/x\n---\nbody\n";
        var path = _project.WriteCmd("0005-without", content);
        var (parsed, _) = CmdFile.Parse(path, "0005-without");

        parsed!.WriteAttempts(1);

        var (cmd, error) = CmdFile.Parse(path, "0005-without");
        Assert.Null(error);
        Assert.Equal(1, cmd!.Attempts);
        Assert.Contains("body", File.ReadAllText(path)); // body untouched
    }

    [Fact]
    public void Writes_DoNotTouchBodyEvenWhenBodyContainsStatusLines()
    {
        var content = "---\ntitle: X\nstatus: ready\nbranch: task/x\nattempts: 0\n---\n\n```yaml\nstatus: ready\nattempts: 0\n```\n";
        var path = _project.WriteCmd("0006-body", content);
        var (cmd, _) = CmdFile.Parse(path, "0006-body");

        cmd!.WriteStatus(CmdStatus.Done);
        cmd.WriteAttempts(3);

        var text = File.ReadAllText(path);
        Assert.Contains("```yaml\nstatus: ready\nattempts: 0\n```", text); // body copy untouched
        Assert.Contains("status: done", text);
        Assert.Contains("attempts: 3", text);
    }

    [Fact]
    public void Parse_HandlesFollowUpSuffixedId_SameAsAPlainOne()
    {
        var path = _project.WriteCmd("0005B-round2", TempProject.CmdMarkdown(branch: "task/0005B-round2"));
        var (cmd, error) = CmdFile.Parse(path, "0005B-round2");

        Assert.Null(error);
        Assert.NotNull(cmd);
        Assert.Equal("0005B", cmd.Number);
        Assert.Equal("round2", cmd.Name);
        Assert.EndsWith("0005B-pln-round2.md", cmd.PlnPath);
        Assert.EndsWith("0005B-qa-round2.md", cmd.QaPath);
        Assert.EndsWith("0005B-rst-round2.md", cmd.RstPath);
        Assert.Equal(
            CmdFile.EvidenceDirectory(_project.Root, "0005B-round2"),
            Path.Combine(_project.Root, ".magnaflow", "0005B-round2"));
    }

    [Fact]
    public void Parse_FollowUpSuffixedId_MissingTitle_FallsBackToNameSlug()
    {
        var content = "---\nstatus: ready\nattempts: 0\n---\nbody";
        var path = _project.WriteCmd("0005B-round-two", content);
        var (cmd, error) = CmdFile.Parse(path, "0005B-round-two");

        Assert.Null(error);
        Assert.Equal("round two", cmd!.Title);
    }

    [Fact]
    public void SiblingPaths_UseTheSameNumberAndName()
    {
        var path = _project.WriteCmd("0001-project-setup", FullCmd);
        var (cmd, _) = CmdFile.Parse(path, "0001-project-setup");

        Assert.EndsWith("0001-pln-project-setup.md", cmd!.PlnPath);
        Assert.EndsWith("0001-qa-project-setup.md", cmd.QaPath);
        Assert.EndsWith("0001-rst-project-setup.md", cmd.RstPath);
    }

    public void Dispose() => _project.Dispose();
}
