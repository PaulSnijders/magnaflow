using MagnaFlow.WorkerController.Execution;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class PromptBuilderTests : IDisposable
{
    private readonly TempProject _project = new();

    private static readonly string Quote = ((char)34).ToString(); // a plain double quote, as it appears in the prompt

    private CmdFile LoadCmd(string[]? specs = null)
    {
        var path = _project.WriteCmd("0001-test", TempProject.CmdMarkdown(specs: specs));
        var (cmd, error) = CmdFile.Parse(path, "0001-test");
        Assert.Null(error);
        return cmd!;
    }

    [Fact]
    public void BuildInitial_IncludesCmdBodyAndSpecContent()
    {
        _project.WriteFile("docs/specs/api.md", "The API must return JSON.");
        var cmd = LoadCmd(specs: ["docs/specs/api.md"]);

        var prompt = PromptBuilder.BuildInitial(cmd, _project.Root, conventions: "");

        Assert.Contains("Do something useful", prompt);
        Assert.Contains("Referenced spec: docs/specs/api.md", prompt);
        Assert.Contains("The API must return JSON.", prompt);
    }

    [Fact]
    public void BuildInitial_TellsTheAgentToOpenTheRstWithFrontmatterIncludingASummary()
    {
        var prompt = PromptBuilder.BuildInitial(LoadCmd(), _project.Root, conventions: "");

        Assert.Contains("0001-rst-test.md", prompt);
        Assert.Contains("cmd: 0001-cmd-test.md", prompt);
        Assert.Contains("summary:", prompt);
        Assert.Contains("what changed, and is there anything I need to", prompt);
        Assert.Contains("do not shorten it", prompt); // the long body stays long
    }

    [Fact]
    public void BuildInitial_AsksForTheSelfAnsweredQuestionsInTheRst()
    {
        var prompt = PromptBuilder.BuildInitial(LoadCmd(), _project.Root, conventions: "");

        Assert.Contains("## Self-answered questions", prompt);
        Assert.Contains("resolved yourself", prompt);
        Assert.Contains("skipped or are still uncertain about", prompt);
    }

    [Fact]
    public void BuildInitial_ContainsTheMagnaflowAndGitGuardrails()
    {
        var prompt = PromptBuilder.BuildInitial(LoadCmd(), _project.Root, conventions: "");

        Assert.Contains(".magnaflow/", prompt);
        Assert.Contains("git commit", prompt);
    }

    [Fact]
    public void BuildInitial_IncludesConventionsWhenPresent()
    {
        var prompt = PromptBuilder.BuildInitial(LoadCmd(), _project.Root, conventions: "Always write tests.");

        Assert.Contains("# Project conventions", prompt);
        Assert.Contains("Always write tests.", prompt);
    }

    [Fact]
    public void BuildInitial_OmitsConventionsSectionWhenEmpty()
    {
        var prompt = PromptBuilder.BuildInitial(LoadCmd(), _project.Root, conventions: "");

        Assert.DoesNotContain("# Project conventions", prompt);
    }

    [Fact]
    public void BuildPlan_AsksForAPlnFileOnlyWhenSomethingIsGenuinelyOpen()
    {
        var cmd = LoadCmd();

        var prompt = PromptBuilder.BuildPlan(cmd, _project.Root, conventions: "");

        Assert.Contains(Path.GetFileName(cmd.PlnPath), prompt);
        Assert.Contains("write it to a file", prompt);                   // the plan itself stays in the session
        Assert.Contains("## Open questions (this round)", prompt);       // the heading PlnFile matches on
        Assert.Contains($"write no `{Path.GetFileName(cmd.PlnPath)}` at all", prompt);
        Assert.Contains("Do something useful", prompt);
    }

    [Fact]
    public void BuildPlan_TellsARePlanToRewriteTheExistingPlnWithoutTheOpenQuestionsSection()
    {
        var cmd = LoadCmd();
        _project.WritePln("0001-test", "# Plan (from the round that paused)");
        _project.WriteQa("0001-test", "## Question (round 1) - answered by the human.");

        var prompt = PromptBuilder.BuildPlan(cmd, _project.Root, conventions: "");

        Assert.Contains(Path.GetFileName(cmd.QaPath), prompt);
        Assert.Contains("read it first", prompt);
        Assert.Contains("already exists from an earlier round", prompt);
        Assert.Contains("WITHOUT any " + Quote + "Open questions" + Quote, prompt);
    }

    [Fact]
    public void FindMissingSpecs_ListsEveryAbsentPath()
    {
        _project.WriteFile("docs/exists.md", "x");
        var cmd = LoadCmd(specs: ["docs/exists.md", "docs/nope1.md", "docs/nope2.md"]);

        var missing = PromptBuilder.FindMissingSpecs(cmd, _project.Root);

        Assert.Equal(["docs/nope1.md", "docs/nope2.md"], missing);
    }

    [Fact]
    public void BuildInitial_ThrowsWhenSpecMissing()
    {
        var cmd = LoadCmd(specs: ["docs/vanished.md"]);

        var ex = Assert.Throws<InvalidOperationException>(() => PromptBuilder.BuildInitial(cmd, _project.Root, conventions: ""));
        Assert.Contains("docs/vanished.md", ex.Message);
    }

    [Fact]
    public void BuildFailureFeedback_WrapsPhaseAndOutput()
    {
        var prompt = PromptBuilder.BuildFailureFeedback("tests", "Assert.Equal() Failure: expected 1, got 2");

        Assert.Contains("tests", prompt);
        Assert.Contains("expected 1, got 2", prompt);
        Assert.Contains(".magnaflow/", prompt); // rules restated on retries
    }

    [Fact]
    public void BuildFailureFeedback_TruncatesHugeOutputKeepingTheTail()
    {
        var output = new string('a', 50_000) + "\nTHE-ACTUAL-ERROR";

        var prompt = PromptBuilder.BuildFailureFeedback("build", output);

        Assert.Contains("THE-ACTUAL-ERROR", prompt);
        Assert.Contains("truncated", prompt);
        Assert.True(prompt.Length < 25_000);
    }

    [Theory]
    [InlineData("plan", "failed while planning")]
    [InlineData("agent", "failed before it finished the implementation")]
    [InlineData("build", "build step failed after your changes")]
    [InlineData("tests", "tests step failed after your changes")]
    public void BuildFailureFeedback_IsWordedPerPhase(string phase, string expected)
    {
        var prompt = PromptBuilder.BuildFailureFeedback(phase, "output");

        Assert.Contains(expected, prompt);
        if (phase is "plan" or "agent")
            Assert.DoesNotContain("after your changes", prompt);
    }

    public void Dispose() => _project.Dispose();
}
