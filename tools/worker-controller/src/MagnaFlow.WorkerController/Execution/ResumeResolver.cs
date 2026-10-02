using System.Text.RegularExpressions;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Execution;

/// <summary>
/// Resolves a command's `resume:` frontmatter value to an agent session ID:
/// - a value that names another command — its id (<c>0008-funnel</c>), or the cmd/sibling
///   filename a human usually has at hand (<c>0008-cmd-funnel</c>,
///   <c>docs/prompts/0008B-rst-funnel.md</c>) — is resolved to that command's recorded session
///   (<c>.magnaflow/&lt;id&gt;/session.yml</c>), so a lineage keeps working even when the session id
///   is re-recorded;
/// - anything else is treated as a raw agent session ID and passed through verbatim
///   (agent-agnostic on purpose — session ID formats differ per agent).
/// A verbatim <c>.magnaflow/&lt;value&gt;/</c> match is tried before any normalization, so a slug that
/// genuinely starts with a lane word still resolves to itself. Unresolvable is a hard error, never
/// a silent fresh session (FR-013a); the message names every id tried and the commands on disk.
/// </summary>
public static class ResumeResolver
{
    // A cmd/pln/qa/rst lane word, stripped ONLY in the infix position — directly after the number
    // and its optional follow-up letter. `0012-fix-qa-export` has `qa` elsewhere, so it stays whole.
    private static readonly Regex LaneInfix =
        new(@"^(\d{4}[B-Z]?)-(?:cmd|pln|qa|rst)-(.+)$", RegexOptions.Compiled);

    public static (string? SessionId, string? Error) Resolve(string resume, string projectRoot)
    {
        // Ordered candidate ids: verbatim first (wins), then the filename normalized to its id.
        var candidates = new List<string>();
        AddCandidate(candidates, resume);
        AddCandidate(candidates, Normalize(resume));

        // Nothing id-shaped to try — this is a raw agent session id; hand it through untouched.
        if (candidates.Count == 0)
            return (resume, null);

        foreach (var candidate in candidates)
        {
            var evidenceDirectory = CmdFile.EvidenceDirectory(projectRoot, candidate);
            if (!Directory.Exists(evidenceDirectory))
                continue;

            var sessionId = SessionEvidence.ReadSessionId(evidenceDirectory);
            return sessionId is null
                ? (null, $"resume: command '{candidate}' has no recorded session yet — it must complete a run first")
                : (sessionId, null);
        }

        return (null, UnresolvableError(resume, candidates, projectRoot));
    }

    private static void AddCandidate(List<string> candidates, string? id)
    {
        if (id is not null && PromptScanner.IdPattern.IsMatch(id) && !candidates.Contains(id))
            candidates.Add(id);
    }

    /// <summary>
    /// A cmd/sibling filename reduced to the command id it names: directory part and a trailing
    /// <c>.md</c> dropped, then a lane word stripped in the infix position only. Returns the value
    /// unchanged when there is nothing to strip; the caller filters non-id shapes out.
    /// </summary>
    private static string Normalize(string resume)
    {
        var name = resume;

        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
            name = name[(lastSeparator + 1)..];

        if (name.EndsWith(".md", StringComparison.Ordinal))
            name = name[..^3];

        var match = LaneInfix.Match(name);
        return match.Success ? $"{match.Groups[1].Value}-{match.Groups[2].Value}" : name;
    }

    private static string UnresolvableError(string resume, IReadOnlyList<string> tried, string projectRoot)
    {
        var triedList = string.Join(", ", tried.Select(id => $"'{id}'"));
        var magnaflow = Path.Combine(projectRoot, ".magnaflow");
        var ran = Directory.Exists(magnaflow)
            ? Directory.EnumerateDirectories(magnaflow)
                .Select(Path.GetFileName)
                .Where(name => name is not null && PromptScanner.IdPattern.IsMatch(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList()
            : [];
        var ranList = ran.Count > 0 ? string.Join(", ", ran) : "(none yet)";
        return $"resume: '{resume}' did not resolve to a command that has run. " +
               $"Tried: {triedList}. Commands that have run: {ranList}";
    }
}
