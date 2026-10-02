namespace MagnaFlow.MfCockpit.Infrastructure;

/// <summary>
/// Path safety (ontwerp-v0.1.md "Guards": "every file the API serves must resolve inside the
/// project root... specs browse takes a user path — normalize and reject escapes"). Used by the
/// specs browser and anywhere else a user-supplied relative path reaches the filesystem.
/// </summary>
public static class PathGuard
{
    /// <summary>Resolves relativePath against root, returning the full path only if it stays
    /// inside root. Rejects rooted/absolute input up front — Path.Combine would otherwise let an
    /// absolute second argument silently replace the root — then re-validates containment after
    /// normalization so ".." segments can't escape either.</summary>
    public static string? ResolveInside(string root, string? relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrEmpty(relativePath))
            return normalizedRoot;

        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
            return null;

        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var isInside = candidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return isInside ? candidate : null;
    }
}
