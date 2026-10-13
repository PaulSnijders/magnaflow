namespace MagnaFlow.WorkerController.Actions;

/// <summary>
/// Resolves an action's `script` to a file on disk, the way mf-run resolves a service `command`
/// (docs/specs/run/mf-run.md#config): the literal path first; an extensionless path then tries
/// `.cmd`, `.bat`, `.exe` on Windows or `.sh` elsewhere. A copy of mf-run's CommandResolver, not a
/// reference to it: the tools never reference each other's assemblies.
/// </summary>
public static class ScriptResolver
{
    private static readonly string[] WindowsFallbackExtensions = [".cmd", ".bat", ".exe"];
    private static readonly string[] UnixFallbackExtensions = [".sh"];

    public sealed record Result(string? Path, IReadOnlyList<string> Attempted);

    public static Result Resolve(string commandPath, bool isWindows)
    {
        var attempted = new List<string> { commandPath };
        if (File.Exists(commandPath))
            return new Result(commandPath, attempted);

        if (!string.IsNullOrEmpty(Path.GetExtension(commandPath)))
            return new Result(null, attempted);

        foreach (var ext in isWindows ? WindowsFallbackExtensions : UnixFallbackExtensions)
        {
            var candidate = commandPath + ext;
            attempted.Add(candidate);
            if (File.Exists(candidate))
                return new Result(candidate, attempted);
        }

        return new Result(null, attempted);
    }
}
