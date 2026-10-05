using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Tests;

public class QaFileTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void AppendQuestions_CreatesTheFileWithAnUnansweredQuestion()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-qa-test.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        QaFile.AppendQuestions(path, ["Which color should the button be?"]);

        Assert.True(QaFile.HasUnansweredQuestion(path));
        var text = File.ReadAllText(path);
        Assert.Contains("## Question (round 1)", text);
        Assert.Contains("Which color should the button be?", text);
        Assert.Contains("**Answer**:", text);
    }

    [Fact]
    public void HasUnansweredQuestion_IsFalseOnceAnswered()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-qa-test.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        QaFile.AppendQuestions(path, ["Which color?"]);

        var answered = File.ReadAllText(path).Replace("**Answer**:", "**Answer**: Blue");
        File.WriteAllText(path, answered);

        Assert.False(QaFile.HasUnansweredQuestion(path));
    }

    [Fact]
    public void HasUnansweredQuestion_IsFalseWhenFileMissing() =>
        Assert.False(QaFile.HasUnansweredQuestion(Path.Combine(_project.Root, "docs", "prompts", "0001-qa-nope.md")));

    [Fact]
    public void AppendQuestions_SecondRound_AppendsToTheSameFileWithIncrementingRound()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-qa-test.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        QaFile.AppendQuestions(path, ["First question?"]);
        var answered = File.ReadAllText(path).Replace("**Answer**:", "**Answer**: Yes");
        File.WriteAllText(path, answered);

        QaFile.AppendQuestions(path, ["Second question?"]);

        var text = File.ReadAllText(path);
        Assert.Contains("## Question (round 1)", text);
        Assert.Contains("## Question (round 2)", text);
        Assert.Contains("First question?", text);
        Assert.Contains("Second question?", text);
        Assert.True(QaFile.HasUnansweredQuestion(path)); // round 2 still unanswered
    }

    [Fact]
    public void ReadLatestAnswers_ReturnsEveryQuestionAnswerPair()
    {
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-qa-test.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        QaFile.AppendQuestions(path, ["Which color?"]);
        var answered = File.ReadAllText(path).Replace("**Answer**:", "**Answer**: Blue");
        File.WriteAllText(path, answered);

        var answers = QaFile.ReadLatestAnswers(path);

        var pair = Assert.Single(answers);
        Assert.Equal("Which color?", pair.Question);
        Assert.Equal("Blue", pair.Answer);
    }

    [Fact]
    public void AppendQuestions_FromThe0019Shape_WritesOneRoundWithNestedLinesIntact()
    {
        var pln = _project.WritePln("0001-test", PlnFileTests.Shape0019);
        var path = Path.Combine(_project.Root, "docs", "prompts", "0001-qa-test.md");

        QaFile.AppendQuestions(path, PlnFile.ReadOpenQuestions(pln));

        var text = File.ReadAllText(path);
        Assert.Contains("## Question (round 1)", text);
        Assert.DoesNotContain("## Question (round 2)", text);
        Assert.Contains("\n  - (A) Do only the prescribed token separation in this run. Keep a", text.Replace("\r\n", "\n"));
        Assert.Contains("\n    that means setsid.", text.Replace("\r\n", "\n"));
        var (question, answer) = Assert.Single(QaFile.ReadLatestAnswers(path));
        Assert.Contains("Recommendation: (A)", question);
        Assert.Equal("", answer);
    }

    public void Dispose() => _project.Dispose();
}
