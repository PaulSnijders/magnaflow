using System.Text;
using System.Text.RegularExpressions;

namespace MagnaFlow.WorkerController.Prompts;

/// <summary>
/// NNNN-qa-name.md — created only when a genuine open question arises (spec FR-011). The
/// controller appends questions (transposed deterministically from the pln file's "Open
/// questions" section — no AI-specific parsing, constitution V); the human writes the answer
/// directly beneath the relevant question. Further pause rounds append further entries to this
/// same file (spec FR-015) — never a new qa file per round.
/// </summary>
public static class QaFile
{
    private static readonly Regex QuestionBlock = new(
        @"^##\s*Question(?:\s*\(round\s*(?<round>\d+)\))?\s*$(?<body>(?:\r?\n(?!##\s).*)*)",
        RegexOptions.Multiline);
    private static readonly Regex AnswerLine = new(@"^\*\*Answer\*\*:\s*(?<answer>.*)$", RegexOptions.Multiline);

    /// <summary>Appends one "## Question (round N)" block per question, continuing the round count already in the file.</summary>
    public static void AppendQuestions(string path, IEnumerable<string> questions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existingRounds = File.Exists(path) ? CountRounds(File.ReadAllText(path)) : 0;
        var sb = new StringBuilder();
        if (File.Exists(path))
        {
            sb.Append(File.ReadAllText(path).TrimEnd());
            sb.AppendLine();
        }

        var round = existingRounds;
        foreach (var question in questions)
        {
            round++;
            sb.AppendLine();
            sb.AppendLine($"## Question (round {round})");
            sb.AppendLine();
            sb.AppendLine(question.Trim());
            sb.AppendLine();
            sb.AppendLine("**Answer**:");
        }

        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>True when the file has at least one question whose Answer line is blank.</summary>
    public static bool HasUnansweredQuestion(string path)
    {
        if (!File.Exists(path))
            return false;

        var text = File.ReadAllText(path);
        foreach (Match block in QuestionBlock.Matches(text))
        {
            var answer = AnswerLine.Match(block.Groups["body"].Value);
            if (!answer.Success || string.IsNullOrWhiteSpace(answer.Groups["answer"].Value))
                return true;
        }
        return false;
    }

    /// <summary>Every question/answer pair currently in the file, in file order.</summary>
    public static IReadOnlyList<(string Question, string Answer)> ReadLatestAnswers(string path)
    {
        if (!File.Exists(path))
            return [];

        var text = File.ReadAllText(path);
        var results = new List<(string, string)>();
        foreach (Match block in QuestionBlock.Matches(text))
        {
            var body = block.Groups["body"].Value;
            var answerMatch = AnswerLine.Match(body);
            var question = answerMatch.Success ? body[..answerMatch.Index].Trim() : body.Trim();
            var answer = answerMatch.Success ? answerMatch.Groups["answer"].Value.Trim() : "";
            results.Add((question, answer));
        }
        return results;
    }

    private static int CountRounds(string text) => QuestionBlock.Matches(text).Count;
}
