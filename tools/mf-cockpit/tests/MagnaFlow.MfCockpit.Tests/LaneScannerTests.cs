using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Tests;

public class LaneScannerTests
{
    [Fact]
    public void Scan_returns_empty_when_prompts_dir_missing()
    {
        using var project = new TempProject();
        Assert.Empty(LaneScanner.Scan(project.Root));
    }

    [Fact]
    public void Scan_parses_cmd_and_groups_siblings_by_id()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "questions", title: "Hello");
        project.WriteSibling("0001-hello", "pln", "# Plan\n\nDo it.");
        project.WriteSibling("0001-hello", "qa", "## Question (round 1)\n\nWhy?\n\n**Answer**:");

        var items = LaneScanner.Scan(project.Root);

        var item = Assert.Single(items);
        Assert.Equal("0001-hello", item.Id);
        Assert.False(item.IsMalformed);
        Assert.Equal(CmdStatus.Questions, item.Cmd!.Status);
        Assert.Equal("Hello", item.Cmd.Title);
        Assert.NotNull(item.PlnPath);
        Assert.NotNull(item.QaPath);
        Assert.Null(item.RstPath);
    }

    [Fact]
    public void Scan_exposes_the_rst_summary_and_leaves_it_null_when_the_report_has_none()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-hello", status: "done");
        project.WriteSibling("0001-hello", "rst", "---\nsummary: The greeting is server-side now.\n---\n\n## What was done\n");
        project.WriteCmd("0002-older", status: "done");
        project.WriteSibling("0002-older", "rst", "## What was done\n\nWritten before summaries existed.\n");
        project.WriteCmd("0003-running", status: "running");

        var items = LaneScanner.Scan(project.Root);

        Assert.Equal("The greeting is server-side now.", items.Single(i => i.Id == "0001-hello").Summary);
        Assert.Null(items.Single(i => i.Id == "0002-older").Summary);
        Assert.Null(items.Single(i => i.Id == "0003-running").Summary); // no rst at all
    }

    [Fact]
    public void Scan_reports_malformed_cmd_without_crashing()
    {
        using var project = new TempProject();
        Directory.CreateDirectory(project.PromptsDir);
        File.WriteAllText(project.CmdPath("0001-broken"), "no frontmatter here");

        var items = LaneScanner.Scan(project.Root);

        var item = Assert.Single(items);
        Assert.True(item.IsMalformed);
        Assert.NotNull(item.Error);
    }

    [Fact]
    public void Scan_orders_by_id()
    {
        using var project = new TempProject();
        project.WriteCmd("0002-second");
        project.WriteCmd("0001-first");

        var items = LaneScanner.Scan(project.Root);

        Assert.Equal(["0001-first", "0002-second"], items.Select(i => i.Id));
    }

    [Fact]
    public void NextFreeNumber_is_0001_when_lane_empty()
    {
        using var project = new TempProject();
        Assert.Equal("0001", LaneScanner.NextFreeNumber(project.Root));
    }

    [Fact]
    public void NextFreeNumber_skips_past_highest_existing_number_including_malformed_and_orphans()
    {
        using var project = new TempProject();
        project.WriteCmd("0001-first");
        project.WriteCmd("0003-third");
        project.WriteSibling("0005-fifth", "rst", "# Report\n\norphaned, no cmd file");

        Assert.Equal("0006", LaneScanner.NextFreeNumber(project.Root));
    }

    [Theory]
    [InlineData("Add Export Button", "add-export-button")]
    [InlineData("  messy!! Title__here  ", "messy-title-here")]
    [InlineData("###", "untitled")]
    public void Slugify_produces_a_valid_id_name(string title, string expected)
    {
        Assert.Equal(expected, LaneScanner.Slugify(title));
    }

    [Fact]
    public void Scan_accepts_followup_suffixed_ids_sorted_between_parent_and_next_number()
    {
        using var project = new TempProject();
        project.WriteCmd("0006-next");
        project.WriteCmd("0005-fix-lava");
        project.WriteCmd("0005B-round2", title: "Round 2");
        project.WriteCmd("0005C-round3");

        var items = LaneScanner.Scan(project.Root);

        Assert.Equal(["0005-fix-lava", "0005B-round2", "0005C-round3", "0006-next"], items.Select(i => i.Id));
        Assert.All(items, i => Assert.False(i.IsMalformed));

        var followUp = items.Single(i => i.Id == "0005B-round2");
        Assert.Equal("0005", followUp.BaseNumber);
        Assert.Equal('B', followUp.Suffix);
        Assert.True(followUp.IsFollowUp);
        Assert.Equal("Round 2", followUp.Cmd!.Title);
        Assert.Equal("0005B", followUp.Cmd.Number);
        Assert.Equal("round2", followUp.Cmd.Name);

        var parent = items.Single(i => i.Id == "0005-fix-lava");
        Assert.Null(parent.Suffix);
        Assert.False(parent.IsFollowUp);
    }

    [Fact]
    public void NextFreeNumber_ignores_followup_suffixed_siblings()
    {
        using var project = new TempProject();
        project.WriteCmd("0005-fix-lava");
        project.WriteCmd("0005B-round2");
        project.WriteCmd("0005C-round3");

        // The base number 0005 is still taken (by the plain sibling); the suffixed follow-ups
        // must never be mistaken for a higher plain number of their own.
        Assert.Equal("0006", LaneScanner.NextFreeNumber(project.Root));
    }

    [Fact]
    public void NextFreeSuffix_is_B_when_only_the_plain_parent_exists()
    {
        using var project = new TempProject();
        project.WriteCmd("0005-fix-lava");

        Assert.Equal('B', LaneScanner.NextFreeSuffix(project.Root, "0005"));
    }

    [Fact]
    public void NextFreeSuffix_skips_past_existing_followups_regardless_of_which_one_was_clicked()
    {
        using var project = new TempProject();
        project.WriteCmd("0005-fix-lava");
        project.WriteCmd("0005B-round2");

        // Continuing 0005B itself still allocates the next letter for the shared base 0005.
        Assert.Equal('C', LaneScanner.NextFreeSuffix(project.Root, "0005"));
    }

    [Fact]
    public void NextFreeSuffix_is_scoped_to_its_own_base_number()
    {
        using var project = new TempProject();
        project.WriteCmd("0005-fix-lava");
        project.WriteCmd("0005B-round2");
        project.WriteCmd("0009-other");

        Assert.Equal('B', LaneScanner.NextFreeSuffix(project.Root, "0009"));
    }
}
