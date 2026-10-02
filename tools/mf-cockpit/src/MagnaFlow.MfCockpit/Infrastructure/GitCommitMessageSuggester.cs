namespace MagnaFlow.MfCockpit.Infrastructure;

/// <summary>
/// Default commit message for the "commit all" button (write #4): the name of the spec file
/// that's part of the pending changes, if there's exactly one — mf-spec's own "spec-first rule"
/// means a behavior change and its owning spec usually land in the same commit, so the spec file
/// is normally the most meaningful one-line description available. Zero or multiple spec files in
/// the same pending change is genuinely ambiguous — no default, the human types their own.
/// </summary>
public static class GitCommitMessageSuggester
{
    public static string? SuggestDefault(IReadOnlyList<string> changedFiles)
    {
        var specFiles = changedFiles
            .Where(f => f.Replace('\\', '/').StartsWith("docs/specs/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return specFiles.Count == 1 ? Path.GetFileName(specFiles[0]) : null;
    }
}
