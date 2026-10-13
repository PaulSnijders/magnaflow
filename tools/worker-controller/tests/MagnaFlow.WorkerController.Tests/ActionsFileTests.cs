using MagnaFlow.WorkerController.Actions;

namespace MagnaFlow.WorkerController.Tests;

public class ActionsFileTests
{
    private static (IReadOnlyList<ActionDefinition>? Actions, string? Error) Parse(string yaml) =>
        ActionsFile.Parse(yaml, "actions.yml");

    [Fact]
    public void Parses_actions_in_file_order_with_defaults()
    {
        var (actions, error) = Parse("""
            actions:
              check-board:
                description: Sync the board
                script: tools/check-board
                args: ["--assignee", "paul"]
                every_minutes: 10
              nightly:
                script: tools/update-deps
                at: "02:30"
                timeout_minutes: 5
              resolve-conflicts:
                prompt: |
                  Resolve it.
            """);

        Assert.Null(error);
        Assert.Equal(["check-board", "nightly", "resolve-conflicts"], actions!.Select(a => a.Name));
        var board = actions[0];
        Assert.Equal("Sync the board", board.Description);
        Assert.Equal(["--assignee", "paul"], board.Args);
        Assert.Equal(10, board.EveryMinutes);
        Assert.Null(board.At);
        Assert.Equal(TimeSpan.FromMinutes(30), board.Timeout);
        Assert.Equal(new TimeOnly(2, 30), actions[1].At);
        Assert.Equal(TimeSpan.FromMinutes(5), actions[1].Timeout);
        Assert.Equal("Resolve it.", actions[2].Prompt?.TrimEnd());
        Assert.Null(actions[2].Script);
        Assert.Empty(actions[2].Args);
    }

    [Theory]
    [InlineData("Bad_Name:\n    script: x", "action 'Bad_Name': name")]
    [InlineData("-lead:\n    script: x", "action '-lead': name")]
    [InlineData("a:\n    description: nothing", "action 'a': script/prompt is missing")]
    [InlineData("a:\n    script: x\n    prompt: y", "action 'a': script/prompt are both set")]
    [InlineData("a:\n    script: x\n    every_minutes: 5\n    at: \"01:00\"", "action 'a': every_minutes/at are both set")]
    [InlineData("a:\n    script: x\n    every_minutes: 0", "action 'a': every_minutes must be a positive integer")]
    [InlineData("a:\n    script: x\n    at: \"25:00\"", "action 'a': at '25:00' is not a time")]
    [InlineData("a:\n    script: x\n    at: \"2:30\"", "action 'a': at '2:30' is not a time")]
    [InlineData("a:\n    script: x\n    timeout_minutes: -1", "action 'a': timeout_minutes must be a positive integer")]
    public void Each_rule_names_the_action_and_the_field(string body, string expected)
    {
        var (actions, error) = Parse("actions:\n  " + body + "\n");

        Assert.Null(actions);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Invalid_yaml_and_a_missing_section_are_errors()
    {
        Assert.Contains("invalid YAML", Parse("actions: [unclosed").Error);
        Assert.Contains("no 'actions:' section", Parse("other: 1").Error);
    }

    [Fact]
    public void A_missing_file_is_an_error()
    {
        using var project = new TempProject();
        var (actions, error) = ActionsFile.Load(project.Root);
        Assert.Null(actions);
        Assert.Contains("no actions file", error);
    }
}
