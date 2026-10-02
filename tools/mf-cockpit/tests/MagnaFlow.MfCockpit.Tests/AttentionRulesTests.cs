using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>The v0.5 item-5 rule: an `aborted` command demands attention only when it is the last
/// command in the lane — no command with a higher ordinal id exists (0005 &lt; 0005B &lt; 0006).
/// `questions` and stale `running` are unchanged and always demand attention.</summary>
public class AttentionRulesTests
{
    private static readonly FakeClock Clock = new();

    [Fact]
    public void Aborted_as_the_highest_id_demands_attention()
    {
        using var project = new TempProject();
        project.WriteCmd("0005-fix-lava", status: "aborted", title: "Fix lava");

        var attention = AttentionRules.Build(project.Root, LaneScanner.Scan(project.Root), Clock);

        var item = Assert.Single(attention);
        Assert.Equal("0005-fix-lava", item.Id);
        Assert.Equal("aborted", item.Status);
    }

    [Fact]
    public void Aborted_with_a_followup_does_not_demand_attention()
    {
        using var project = new TempProject();
        project.WriteCmd("0005-fix-lava", status: "aborted");
        project.WriteCmd("0005B-round2", status: "draft");

        var attention = AttentionRules.Build(project.Root, LaneScanner.Scan(project.Root), Clock);

        Assert.Empty(attention);
    }

    [Fact]
    public void Aborted_followed_by_an_unrelated_higher_number_does_not_demand_attention()
    {
        using var project = new TempProject();
        project.WriteCmd("0005-fix-lava", status: "aborted");
        project.WriteCmd("0006-something-else", status: "draft");

        var attention = AttentionRules.Build(project.Root, LaneScanner.Scan(project.Root), Clock);

        Assert.Empty(attention);
    }

    [Fact]
    public void Questions_always_demands_attention_even_when_not_the_last_command()
    {
        using var project = new TempProject();
        project.WriteCmd("0005-ask", status: "questions", title: "Ask");
        project.WriteCmd("0006-next", status: "draft");

        var attention = AttentionRules.Build(project.Root, LaneScanner.Scan(project.Root), Clock);

        var item = Assert.Single(attention);
        Assert.Equal("0005-ask", item.Id);
        Assert.Equal("questions", item.Status);
    }
}
