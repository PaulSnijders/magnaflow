using System.Text;
using System.Text.RegularExpressions;

namespace MagnaFlow.WorkerController.Prompts;

/// <summary>
/// NNNN-pln-name.md — executor-owned (spec FR-009): written only for a round that has an open
/// question, and rewritten in place on re-plan (contracts/file-formats.md). A well-specified
/// command produces no pln at all — the plan itself lives in the agent's session, and the file
/// exists to carry the question. The agent authors it via its own file-editing tools; the
/// controller only reads it back (`ReadOpenQuestions`) to decide the plan/execution gate, and
/// an absent file simply means nothing was open. `Write` exists for test fixtures and as a
/// controller-owned fallback shape, not as the agent's own authoring path.
/// </summary>
public static class PlnFile
{
    private static readonly Regex OpenQuestionsSection = new(
        @"^##\s*Open questions.*$(?<body>(?:\r?\n(?!##\s).*)*)", RegexOptions.Multiline);
    private static readonly Regex BulletStart = new(@"^(?<indent>[ \t]*)-\s+(?<text>.*)$");

    public static void Write(
        string path,
        string planText,
        IReadOnlyList<(string Question, string Answer)>? selfAnswered = null,
        IReadOnlyList<string>? openQuestions = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.AppendLine("# Plan");
        sb.AppendLine();
        sb.AppendLine(planText.Trim());

        if (selfAnswered is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Self-answered questions");
            sb.AppendLine();
            foreach (var (question, answer) in selfAnswered)
                sb.AppendLine($"- Q: {question} → A: {answer}");
        }

        if (openQuestions is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Open questions (this round)");
            sb.AppendLine();
            foreach (var question in openQuestions)
                sb.AppendLine($"- {question}");
        }

        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Extracts the "Open questions (this round)" section as a list of questions; empty when
    /// absent or the file doesn't exist. One question is one top-level bullet together with
    /// everything under it — continuation lines, nested bullets, and any text after the list,
    /// which belongs to the last question — up to the next top-level bullet or the end of the
    /// section. The top level is the indent of the section's first bullet; text before it is
    /// not a question. Only the top-level bullet marker is dropped: every other line is kept
    /// verbatim, so nested lines stay nested in the qa file.
    /// </summary>
    public static IReadOnlyList<string> ReadOpenQuestions(string path)
    {
        if (!File.Exists(path))
            return [];

        var text = File.ReadAllText(path);
        var section = OpenQuestionsSection.Match(text);
        if (!section.Success)
            return [];

        var questions = new List<List<string>>();
        int? topIndent = null;
        foreach (var raw in section.Groups["body"].Value.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var bullet = BulletStart.Match(line);
            if (bullet.Success && (topIndent is null || bullet.Groups["indent"].Length <= topIndent))
            {
                topIndent ??= bullet.Groups["indent"].Length;
                questions.Add([bullet.Groups["text"].Value]);
            }
            else if (questions.Count > 0)
            {
                questions[^1].Add(line);
            }
        }

        return [.. questions
            .Select(lines => string.Join(Environment.NewLine, lines.Select(l => l.TrimEnd())).Trim())
            .Where(q => q.Length > 0)];
    }
}
