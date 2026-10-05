using System.Globalization;
using System.Text;

namespace MagnaFlow.WorkerController.Prompts;

/// <summary>
/// NNNN-rst-name.md — executor-owned (spec FR-017): written or updated only when a run's
/// execution phase reaches a terminal outcome (done/aborted), never for a run ending questions
/// (FR-019). The agent authors it via its own file-editing tools, same ownership model as the
/// pln file; the controller only checks existence and, as a safety net, writes a minimal
/// fallback report if the agent's own instructions were not followed (mirrors v0.1's
/// ResultWriter fallback-summary behavior — "MUST ensure the rst is written" is a controller
/// obligation even when the agent doesn't cooperate).
///
/// Every rst opens with the same frontmatter a hand-written one has — title/cmd/done plus the
/// one-line `summary:` that is the only human-sized layer over a report written for the next AI
/// session (docs/decisions/0015-rst-summary-line.md).
/// </summary>
public static class RstFile
{
    public static bool Exists(string path) => File.Exists(path);

    /// <summary>
    /// Writes a complete rst: frontmatter + report body. <paramref name="title"/> defaults to the
    /// file's own name slug (same fallback shape as CmdFile.Title), <paramref name="summary"/> to
    /// the first line of <paramref name="whatWasDone"/> — the field is never silently omitted, so
    /// a controller-written report is as readable in the lane as an agent-written one.
    /// </summary>
    public static void Write(
        string path,
        string whatWasDone,
        string? decisions = null,
        string? abortReason = null,
        string? title = null,
        string? summary = null,
        DateTimeOffset? done = null)
    {
        var fileName = Path.GetFileName(path);
        var frontmatter = new StringBuilder();
        frontmatter.AppendLine("---");
        frontmatter.AppendLine($"title: {YamlScalar(title ?? TitleFromFileName(fileName))}");
        frontmatter.AppendLine($"cmd: {CmdFileNameFor(fileName)}");
        frontmatter.AppendLine($"done: {(done ?? DateTimeOffset.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        frontmatter.AppendLine($"summary: {YamlScalar(summary ?? FirstLine(whatWasDone))}");
        frontmatter.AppendLine("---");

        var body = $"""
            # Report

            ## What was done

            {whatWasDone.Trim()}
            """;

        if (!string.IsNullOrWhiteSpace(decisions))
            body += $"""


                ## Decisions and deviations from the plan

                {decisions.Trim()}
                """;

        if (!string.IsNullOrWhiteSpace(abortReason))
            body += $"""


                ## Abort reason

                {abortReason.Trim()}
                """;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, frontmatter.ToString() + Environment.NewLine + body + Environment.NewLine);
    }

    /// <summary>
    /// Controller-authored safety net when the agent produced no rst of its own (spec FR-017's
    /// "MUST ensure"). A re-run after an earlier abort says so in the first line of "What was done".
    /// </summary>
    public static void WriteFallback(string path, bool succeeded, int attempts, string? abortReason, string? title = null, bool rerunAfterAbort = false) =>
        Write(
            path,
            (rerunAfterAbort ? "Re-run after an earlier `aborted`: a human set the command back to `ready`, which reset its attempts.\n\n" : "")
            + (succeeded
                ? $"Command completed after {attempts} attempt(s); see claude.log for details."
                : $"Command aborted after {attempts} attempt(s); see claude.log for details."),
            decisions: null,
            abortReason: succeeded ? null : abortReason ?? "unknown",
            title: title,
            summary: succeeded
                ? $"Completed after {attempts} attempt(s), but the agent wrote no report of its own — read claude.log to see what happened."
                : $"Aborted after {attempts} attempt(s) and the agent wrote no report of its own — read claude.log before deciding what to do next.");

    /// <summary>Appends a warning section to an already-written rst (ontwerp-v0.1.md, mf-run
    /// "Worker integration": a post-terminal `mf-run start` failure is "a warning, never a retry" —
    /// it must not overwrite the agent's own report, only add to it.</summary>
    public static void AppendWarning(string path, string warning)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path) : "# Report\n";
        var section = $"""


            ## Warning

            {warning.Trim()}
            """;
        File.WriteAllText(path, existing.TrimEnd() + Environment.NewLine + section + Environment.NewLine);
    }

    /// <summary>"0007-rst-add-export-button.md" -&gt; "0007-cmd-add-export-button.md": the rst's
    /// frontmatter points back at the command it reports on.</summary>
    private static string CmdFileNameFor(string rstFileName)
    {
        var marker = rstFileName.IndexOf("-rst-", StringComparison.Ordinal);
        return marker < 0 ? rstFileName : string.Concat(rstFileName.AsSpan(0, marker), "-cmd-", rstFileName.AsSpan(marker + 5));
    }

    /// <summary>"0007-rst-add-export-button.md" -&gt; "add export button" — same name-slug fallback
    /// CmdFile.Title uses when a command has no title of its own.</summary>
    private static string TitleFromFileName(string rstFileName)
    {
        var stem = Path.GetFileNameWithoutExtension(rstFileName);
        var marker = stem.IndexOf("-rst-", StringComparison.Ordinal);
        return (marker < 0 ? stem : stem[(marker + 5)..]).Replace('-', ' ');
    }

    private static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        var end = trimmed.IndexOfAny(['\r', '\n']);
        return end < 0 ? trimmed : trimmed[..end];
    }

    /// <summary>Emits a YAML scalar, double-quoting only when the raw text would not survive as a
    /// plain one. A summary or title with a colon in it ("mf-run: robust status") is entirely
    /// normal prose, and unquoted it would silently turn the whole frontmatter into invalid YAML —
    /// which is exactly the frontmatter the cockpit reads back.</summary>
    private static string YamlScalar(string value)
    {
        var text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0)
            return "\"\"";

        var needsQuoting = text.Contains(": ") || text.EndsWith(':') || text.Contains(" #")
            || text.Contains('"') || text.Contains('\\') || "-?:,[]{}#&*!|>'\"%@`".Contains(text[0]);

        return needsQuoting ? $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"" : text;
    }
}
