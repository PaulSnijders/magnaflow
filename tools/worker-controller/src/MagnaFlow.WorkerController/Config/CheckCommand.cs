namespace MagnaFlow.WorkerController.Config;

/// <summary>
/// One build or test command (docs/specs/concepts/machine-config.md "Path-scoped commands").
/// <see cref="Paths"/> null = always runs; otherwise it runs only when the run touched a file
/// under one of these repo-relative prefixes (a trailing '/' marks a folder).
/// </summary>
public sealed record CheckCommand(string Run, IReadOnlyList<string>? Paths = null)
{
    public static implicit operator CheckCommand(string run) => new(run);

    public bool IsPathScoped => Paths is { Count: > 0 };

    /// <summary>Whether this command applies to the touched files. A path entry ending in '/'
    /// matches everything below it; one without matches that exact file or, if it is a folder,
    /// everything below it — never a mere name prefix (`topo` does not match `topology.md`).</summary>
    public bool AppliesTo(IReadOnlyCollection<string> touchedPaths) =>
        !IsPathScoped || touchedPaths.Any(t => Paths!.Any(p => Matches(p, t)));

    public static bool Matches(string pathEntry, string touchedPath) =>
        pathEntry.EndsWith('/')
            ? touchedPath.StartsWith(pathEntry, StringComparison.Ordinal)
            : touchedPath == pathEntry || touchedPath.StartsWith(pathEntry + "/", StringComparison.Ordinal);

    /// <summary>"topo/, more/x.js" — for log lines and the prompt.</summary>
    public string PathList => string.Join(", ", Paths ?? []);
}
