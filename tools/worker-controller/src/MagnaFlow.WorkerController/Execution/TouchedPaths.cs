using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Execution;

/// <summary>
/// The files a run touched, as the path-scoped build/test commands see them
/// (docs/specs/concepts/worker-run.md step 5): git's list minus the command's own lane files and
/// its evidence folder — bookkeeping, not the agent's change.
/// </summary>
public sealed class TouchedPaths
{
    public const string ConfigFile = ".magnaflow/config.yml";

    public IReadOnlyList<string> Paths { get; }

    /// <summary>The project config itself changed: every command runs, scoped or not.</summary>
    public bool ForcesAll => Paths.Contains(ConfigFile);

    private TouchedPaths(IReadOnlyList<string> paths) => Paths = paths;

    public static TouchedPaths From(IEnumerable<string> gitPaths, CmdFile cmd, string projectRoot)
    {
        string Rel(string absolute) => Path.GetRelativePath(projectRoot, absolute).Replace('\\', '/');
        var lane = new HashSet<string>(StringComparer.Ordinal)
        {
            Rel(cmd.FilePath), Rel(cmd.PlnPath), Rel(cmd.QaPath), Rel(cmd.RstPath),
        };
        var evidence = Rel(CmdFile.EvidenceDirectory(projectRoot, cmd.Id)) + "/";
        return new TouchedPaths(gitPaths
            .Select(p => p.Replace('\\', '/'))
            .Where(p => !lane.Contains(p) && !p.StartsWith(evidence, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>For tests and callers that already filtered.</summary>
    public static TouchedPaths Of(params string[] paths) => new(paths);

    public bool Runs(CheckCommand command) => ForcesAll || command.AppliesTo(Paths);

    /// <summary>"touched paths: 4 (topo/ 3, (root) 1)" — the count per top-level folder.</summary>
    public string Summary()
    {
        if (Paths.Count == 0)
            return "touched paths: 0";
        var groups = Paths
            .GroupBy(p => p.Contains('/') ? p[..(p.IndexOf('/') + 1)] : "(root)")
            .OrderBy(g => g.Key == "(root)" ? 1 : 0).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} {g.Count()}");
        return $"touched paths: {Paths.Count} ({string.Join(", ", groups)})";
    }
}
