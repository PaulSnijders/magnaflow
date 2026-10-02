using System.Text;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Execution;

/// <summary>
/// Builds agent prompts from the command body, the full content of linked spec files (v0.1
/// FR-007), and the project's own conventions (spec FR-020). The controller never interprets
/// any of this content — it only assembles text (constitution V).
/// </summary>
public static class PromptBuilder
{
    private const int MaxFeedbackChars = 16_000;

    /// <summary>Guardrails shared by every phase (research R6, spec FR-005): the controller owns all commits and the cmd file itself.</summary>
    private const string Preamble =
        """
        You are executing one delegated command inside this repository.

        Rules:
        - Implement exactly what the command below describes; stay inside its scope.
        - NEVER create, modify, or delete anything under the .magnaflow/ folder.
        - NEVER modify the command's own frontmatter or body (the docs/prompts/*-cmd-*.md file) — it is human-owned.
        - You may read docs/prompts/*-qa-*.md for the human's answers, but never write to it — the controller and the human own it.
        - NEVER run git commit, git branch, git checkout, or git push; the controller owns all git operations.
        - The project's build and tests will be run after you finish implementing; they must pass.
        """;

    /// <summary>Returns the spec paths that do not exist; the run must abort before the agent starts when non-empty.</summary>
    public static IReadOnlyList<string> FindMissingSpecs(CmdFile cmd, string projectRoot) =>
        cmd.Specs.Where(s => !File.Exists(Path.Combine(projectRoot, s))).ToList();

    private static string BuildConventionsAndSpecs(CmdFile cmd, string projectRoot, string conventions)
    {
        var missing = FindMissingSpecs(cmd, projectRoot);
        if (missing.Count > 0)
            throw new InvalidOperationException($"linked spec file(s) not found: {string.Join(", ", missing)}");

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(conventions))
        {
            sb.AppendLine("# Project conventions");
            sb.AppendLine();
            sb.AppendLine(conventions.Trim());
            sb.AppendLine();
        }

        foreach (var spec in cmd.Specs)
        {
            sb.AppendLine($"---");
            sb.AppendLine($"# Referenced spec: {spec}");
            sb.AppendLine();
            sb.AppendLine(File.ReadAllText(Path.Combine(projectRoot, spec)).Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// The plan-phase prompt (spec FR-008/FR-009): the plan itself is produced in the agent's
    /// reply and stays in the session it will implement from. The pln file is written only when
    /// the round has an open question — or when a pln/qa already exists and a leftover
    /// "Open questions" section has to be dropped.
    /// </summary>
    public static string BuildPlan(CmdFile cmd, string projectRoot, string conventions)
    {
        var pln = Path.GetFileName(cmd.PlnPath);
        var qa = Path.GetFileName(cmd.QaPath);
        var plnExists = File.Exists(cmd.PlnPath);
        var qaExists = File.Exists(cmd.QaPath);

        var prompt = new StringBuilder();
        prompt.AppendLine(Preamble);
        prompt.AppendLine();
        prompt.AppendLine("Before making any change, plan.");
        prompt.AppendLine();

        if (qaExists)
        {
            prompt.AppendLine(
                $"""
                A `{qa}` file already exists next to this command: read it first. It holds the
                human's answers to questions a previous round raised — incorporate them.
                """);
            prompt.AppendLine();
        }

        prompt.AppendLine(
            """
            Set your plan out in this reply: the intended approach, plus every question the
            command left open that you resolved yourself from the linked specs, the existing
            code, or the project conventions below, with the answer you settled on. The plan
            stays in this session — you will implement from it in the same session — so do not
            write it to a file.
            """);
        prompt.AppendLine();

        prompt.AppendLine(
            $"""
            If, after that, something genuinely requires a human decision (a product choice, a
            missing preference, an ambiguous priority that the specs, the code and the
            conventions do not settle), do NOT guess. Instead write `{pln}` (same folder as this
            command, replacing any content already there) with your plan followed by a section
            headed exactly:

            ## Open questions (this round)

            one bullet per open question — then stop, and make no code change this run. That
            file is the controller's only signal that you are waiting on the human.
            """);
        prompt.AppendLine();

        if (plnExists || qaExists)
        {
            prompt.AppendLine(
                $"""
                `{pln}` already exists from an earlier round of this command. If nothing is open
                any more, rewrite it with your current plan and WITHOUT any "Open questions"
                section — a leftover section pauses the run again and the command is never
                implemented. Then end your response; the implementation instruction will follow.
                """);
        }
        else
        {
            prompt.AppendLine(
                $"""
                If nothing is genuinely open, write no `{pln}` at all and end your response after
                the plan; the implementation instruction will follow.
                """);
        }

        prompt.AppendLine();
        prompt.AppendLine(BuildConventionsAndSpecs(cmd, projectRoot, conventions));
        prompt.AppendLine($"# Command: {cmd.Title}");
        prompt.AppendLine();
        prompt.AppendLine(cmd.Body.Trim());

        return prompt.ToString();
    }

    /// <summary>The implementation instruction, sent once the plan/question gate has passed (spec FR-010).</summary>
    public static string BuildInitial(CmdFile cmd, string projectRoot, string conventions)
    {
        var rst = Path.GetFileName(cmd.RstPath);
        var cmdFile = Path.GetFileName(cmd.FilePath);

        var prompt = new StringBuilder();
        prompt.AppendLine(Preamble);
        prompt.AppendLine();
        prompt.AppendLine(
            $"""
            Your plan for this command raised nothing that needs a human decision — implement it
            now, following the plan you just wrote. When you conclude (success or not), write or
            update `{rst}` (same folder as this command): what you did, the decisions you took
            while implementing, and anything you skipped or are still uncertain about — written
            for the next session to read, not as a restatement of the plan.

            Include a "## Self-answered questions" section: every question this command left
            open that you resolved yourself — from the linked specs, the existing code, or the
            project conventions — with the answer you settled on. That section is what tells a
            human their command was under-specified. Say so explicitly if there were none.

            Open `{rst}` with YAML frontmatter, exactly these four fields:

            ---
            title: "{cmd.Title}"
            cmd: {cmdFile}
            done: <today, YYYY-MM-DD>
            summary: <one line — see below>
            ---

            `summary:` is the human-sized layer over a report that is otherwise written for the
            next session to read: it is what the cockpit shows under this command in the project
            lane, and it is all a human sees at a glance. One or two sentences of plain text on a
            single line, no markdown, answering "what changed, and is there anything I need to
            do?". Quote the value if it contains a colon. The report body below the frontmatter
            stays exactly what it would otherwise be — do not shorten it.
            """);
        prompt.AppendLine();
        prompt.AppendLine(BuildConventionsAndSpecs(cmd, projectRoot, conventions));
        prompt.AppendLine($"# Command: {cmd.Title}");
        prompt.AppendLine();
        prompt.AppendLine(cmd.Body.Trim());

        return prompt.ToString();
    }

    /// <summary>Follow-up prompt wrapping build/test failure output (v0.1 FR-010), sent into the same session.</summary>
    public static string BuildFailureFeedback(string phase, string failureOutput)
    {
        var output = failureOutput.Length > MaxFeedbackChars
            ? "[...output truncated...]\n" + failureOutput[^MaxFeedbackChars..]
            : failureOutput;

        return
            $"""
            The {phase} step failed after your changes. Analyze the output below, fix the problems,
            and make the {phase} pass. The same rules apply: never touch .magnaflow/ and never run git commands.

            {phase} output:
            ```
            {output.Trim()}
            ```
            """;
    }
}
