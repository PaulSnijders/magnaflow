namespace MagnaFlow.WorkerController.Execution;

/// <summary>
/// Loads the target project's own conventions so the worker inherits the same guardrails a
/// human session has via CLAUDE.md (spec FR-020/FR-021). Fixed discovery order, no
/// configuration: CLAUDE.md at the repository root, then a constitution file — checked at
/// .specify/memory/constitution.md, then docs/constitution.md, first hit wins. CLAUDE.md and a
/// constitution file are independent (both included when both exist); only the two constitution
/// candidate paths are mutually exclusive. Missing files are silently skipped — this is
/// read-only, additive input, never an error.
/// </summary>
public static class ConventionLoader
{
    /// <summary>Concatenated content of whichever convention files exist; empty string when none do.</summary>
    public static string Load(string projectRoot)
    {
        var parts = new List<string>();

        var claudeMd = Path.Combine(projectRoot, "CLAUDE.md");
        if (File.Exists(claudeMd))
            parts.Add(File.ReadAllText(claudeMd).Trim());

        var constitutionPath = new[]
        {
            Path.Combine(projectRoot, ".specify", "memory", "constitution.md"),
            Path.Combine(projectRoot, "docs", "constitution.md"),
        }.FirstOrDefault(File.Exists);
        if (constitutionPath is not null)
            parts.Add(File.ReadAllText(constitutionPath).Trim());

        return string.Join("\n\n---\n\n", parts);
    }
}
