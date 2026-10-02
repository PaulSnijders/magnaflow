using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Specs;

public sealed record SpecsEntry(string Name, bool IsDirectory);

public abstract record SpecsResult
{
    public sealed record Directory(IReadOnlyList<SpecsEntry> Entries) : SpecsResult;
    public sealed record FileContent(string Content) : SpecsResult;
    /// <summary>relativePath escaped the specs root, or contained a rooted/drive segment.</summary>
    public sealed record Escaped : SpecsResult;
    public sealed record NotFound : SpecsResult;
}

/// <summary>
/// Read-only browse of docs/specs/ (docs/mf-spec/system.md) — rendered client-side, the cockpit
/// never edits specs (ontwerp-v0.1.md "Invariant"). Path safety is the whole point of this class:
/// relativePath is user input from the URL.
/// </summary>
public static class SpecsBrowser
{
    public static string SpecsRoot(string projectRoot) => Path.Combine(projectRoot, "docs", "specs");

    public static SpecsResult Browse(string projectRoot, string? relativePath)
    {
        var root = SpecsRoot(projectRoot);
        var resolved = PathGuard.ResolveInside(root, relativePath);
        if (resolved is null)
            return new SpecsResult.Escaped();

        if (System.IO.Directory.Exists(resolved))
        {
            var entries = System.IO.Directory.EnumerateFileSystemEntries(resolved)
                .Select(p => new SpecsEntry(Path.GetFileName(p)!, System.IO.Directory.Exists(p)))
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new SpecsResult.Directory(entries);
        }

        if (File.Exists(resolved))
            return new SpecsResult.FileContent(File.ReadAllText(resolved));

        return new SpecsResult.NotFound();
    }
}
