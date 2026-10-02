using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Tests;

public class DraftWriterTests
{
    [Fact]
    public async Task CreateDraftAsync_writes_draft_status_and_commits_it()
    {
        using var project = new TempProject();
        var git = new FakeGitClient();
        var writer = new DraftWriter(git, new FakeClock());

        var result = await writer.CreateDraftAsync(project.Root, "Add export button", "## Goal\nExport.", group: null, specs: null);

        Assert.Equal("0001-add-export-button", result.Id);
        Assert.True(File.Exists(result.FilePath));
        var content = File.ReadAllText(result.FilePath);
        Assert.Contains("status: draft", content);
        Assert.Contains("title: Add export button", content);
        Assert.Contains("created: 2026-07-13", content);

        var commit = Assert.Single(git.Commits);
        Assert.Equal("cockpit: create draft 0001-add-export-button", commit.Message);
    }

    [Fact]
    public async Task CreateDraftAsync_picks_the_next_free_number()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-existing");
        project.WriteCmd("0002-another");
        var writer = new DraftWriter(new FakeGitClient(), new FakeClock());

        var result = await writer.CreateDraftAsync(project.Root, "Third one", "", null, null);

        Assert.StartsWith("0003-", result.Id);
    }

    [Fact]
    public async Task CreateDraftAsync_rejects_empty_title()
    {
        using var project = new TempProject();
        var writer = new DraftWriter(new FakeGitClient(), new FakeClock());

        await Assert.ThrowsAsync<ArgumentException>(() => writer.CreateDraftAsync(project.Root, "   ", "body", null, null));
    }

    [Fact]
    public async Task FlipToReadyAsync_flips_draft_to_ready_and_commits()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "draft");
        var git = new FakeGitClient();
        var writer = new DraftWriter(git, new FakeClock());

        var outcome = await writer.FlipToReadyAsync(project.Root, "0001-hello");

        Assert.Equal(DraftWriter.ReadyOutcome.Ok, outcome);
        var content = File.ReadAllText(project.CmdPath("0001-hello"));
        Assert.Contains("status: ready", content);
        Assert.DoesNotContain("status: draft", content);

        var commit = Assert.Single(git.Commits);
        Assert.Equal("cockpit: ready 0001-hello", commit.Message);
    }

    [Fact]
    public async Task FlipToReadyAsync_preserves_everything_else_byte_for_byte()
    {
        using var project = new TempProject();
        var path = project.WriteCmd("0001-hello", status: "draft", title: "Hello world");
        var before = File.ReadAllText(path);
        var writer = new DraftWriter(new FakeGitClient(), new FakeClock());

        await writer.FlipToReadyAsync(project.Root, "0001-hello");

        var after = File.ReadAllText(path);
        Assert.Equal(before.Replace("status: draft", "status: ready"), after);
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("running")]
    [InlineData("questions")]
    [InlineData("done")]
    [InlineData("aborted")]
    public async Task FlipToReadyAsync_refuses_any_status_other_than_draft(string status)
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: status);
        var git = new FakeGitClient();
        var writer = new DraftWriter(git, new FakeClock());

        var outcome = await writer.FlipToReadyAsync(project.Root, "0001-hello");

        Assert.Equal(DraftWriter.ReadyOutcome.WrongStatus, outcome);
        Assert.Empty(git.Commits);
    }

    [Fact]
    public async Task FlipToReadyAsync_returns_not_found_for_unknown_id()
    {
        using var project = new TempProject();
        var writer = new DraftWriter(new FakeGitClient(), new FakeClock());

        var outcome = await writer.FlipToReadyAsync(project.Root, "9999-nope");

        Assert.Equal(DraftWriter.ReadyOutcome.NotFound, outcome);
    }

    private static string WriteParentWithFields(TempProject project, string id, string status = "done")
    {
        Directory.CreateDirectory(project.PromptsDir);
        var content = $"""
            ---
            title: Fix the lava colors
            status: {status}
            branch: task/{id}
            base: main
            group: 001-visuals
            specs:
              - docs/specs/design.md
            attempts: 1
            ---

            ## Goal
            Fix it.
            """;
        var (num, name) = TempProject.SplitId(id);
        var path = Path.Combine(project.PromptsDir, $"{num}-cmd-{name}.md");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task CreateFollowUpAsync_copies_parent_frontmatter_and_sets_resume_from_session()
    {
        using var project = new TempProject();
        WriteParentWithFields(project, "0005-fix-lava");
        project.WriteEvidence("0005-fix-lava", "session.yml", "session: sess-abc-123\n");
        var git = new FakeGitClient();
        var writer = new DraftWriter(git, new FakeClock());

        var (outcome, result) = await writer.CreateFollowUpAsync(project.Root, "0005-fix-lava", "this line should be orange, not red", slug: null);

        Assert.Equal(DraftWriter.FollowUpOutcome.Ok, outcome);
        Assert.Equal("0005B-fix-lava", result!.Id);
        Assert.True(result.ResumeSet);
        Assert.Null(result.Warning);

        var content = File.ReadAllText(result.FilePath);
        Assert.Contains("status: draft", content);
        Assert.Contains("branch: task/0005-fix-lava", content);
        Assert.Contains("base: main", content);
        Assert.Contains("group: 001-visuals", content);
        Assert.Contains("docs/specs/design.md", content);
        Assert.Contains("resume: sess-abc-123", content);
        Assert.Contains("this line should be orange, not red", content);
        Assert.Contains("0005-fix-lava", content); // reference back to the parent

        var commit = Assert.Single(git.Commits);
        Assert.Contains("0005B-fix-lava", commit.Message);
        Assert.Contains("0005-fix-lava", commit.Message);
    }

    [Fact]
    public async Task CreateFollowUpAsync_uses_a_custom_slug_when_given()
    {
        using var project = new TempProject();
        WriteParentWithFields(project, "0005-fix-lava");
        var writer = new DraftWriter(new FakeGitClient(), new FakeClock());

        var (_, result) = await writer.CreateFollowUpAsync(project.Root, "0005-fix-lava", "feedback", slug: "Round Two!");

        Assert.Equal("0005B-round-two", result!.Id);
    }

    [Fact]
    public async Task CreateFollowUpAsync_allocates_next_letter_regardless_of_which_ancestor_was_clicked()
    {
        using var project = new TempProject();
        WriteParentWithFields(project, "0005-fix-lava");
        var writer = new DraftWriter(new FakeGitClient(), new FakeClock());
        await writer.CreateFollowUpAsync(project.Root, "0005-fix-lava", "first follow-up", slug: null);

        // Continuing the follow-up itself (not the root) still shares the same base number.
        var (outcome, result) = await writer.CreateFollowUpAsync(project.Root, "0005B-fix-lava", "second follow-up", slug: null);

        Assert.Equal(DraftWriter.FollowUpOutcome.Ok, outcome);
        Assert.Equal("0005C-fix-lava", result!.Id);
    }

    [Fact]
    public async Task CreateFollowUpAsync_omits_resume_and_warns_when_parent_has_no_session()
    {
        using var project = new TempProject();
        WriteParentWithFields(project, "0005-fix-lava");
        var writer = new DraftWriter(new FakeGitClient(), new FakeClock());

        var (outcome, result) = await writer.CreateFollowUpAsync(project.Root, "0005-fix-lava", "feedback", slug: null);

        Assert.Equal(DraftWriter.FollowUpOutcome.Ok, outcome);
        Assert.False(result!.ResumeSet);
        Assert.NotNull(result.Warning);
        Assert.DoesNotContain("resume:", File.ReadAllText(result.FilePath));
    }

    [Fact]
    public async Task CreateFollowUpAsync_returns_parent_not_found_for_unknown_parent()
    {
        using var project = new TempProject();
        var git = new FakeGitClient();
        var writer = new DraftWriter(git, new FakeClock());

        var (outcome, result) = await writer.CreateFollowUpAsync(project.Root, "9999-nope", "feedback", slug: null);

        Assert.Equal(DraftWriter.FollowUpOutcome.ParentNotFound, outcome);
        Assert.Null(result);
        Assert.Empty(git.Commits);
    }

    [Fact]
    public async Task CreateFollowUpAsync_rejects_empty_feedback()
    {
        using var project = new TempProject();
        WriteParentWithFields(project, "0005-fix-lava");
        var writer = new DraftWriter(new FakeGitClient(), new FakeClock());

        await Assert.ThrowsAsync<ArgumentException>(() => writer.CreateFollowUpAsync(project.Root, "0005-fix-lava", "   ", null));
    }
}
