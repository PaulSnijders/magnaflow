namespace MagnaFlow.MfRun.Runtime;

/// <summary>
/// Resolves a service's configured `command` to an actual file on disk (docs/prompts/0004
/// "cross-platform service commands"). A command with an extension is resolved exactly as
/// before — an exists-check on that literal path, no fallback, so existing configs are
/// unaffected. A command with no extension additionally tries per-OS wrapper suffixes, in
/// order, before giving up: Windows tries `.cmd`, `.bat`, `.exe`; Unix tries `.sh` (the bare
/// literal is always tried first on every OS). This lets one config.yml reference an
/// extensionless command that resolves to whichever OS-specific wrapper is actually committed
/// next to it.
/// </summary>
public static class CommandResolver
{
    private static readonly string[] WindowsFallbackExtensions = [".cmd", ".bat", ".exe"];
    private static readonly string[] UnixFallbackExtensions = [".sh"];

    /// <summary>Attempted lists every path tried, in order, whether or not resolution
    /// succeeded — the not-found error message shows exactly what was tried. ResolvedViaFallback
    /// is false when the literal path itself resolved (nothing to log).</summary>
    public sealed record Result(string? Path, IReadOnlyList<string> Attempted, bool ResolvedViaFallback);

    public static Result Resolve(string commandPath, bool isWindows)
    {
        var attempted = new List<string> { commandPath };
        if (File.Exists(commandPath))
            return new Result(commandPath, attempted, false);

        if (!string.IsNullOrEmpty(Path.GetExtension(commandPath)))
            return new Result(null, attempted, false);

        foreach (var ext in isWindows ? WindowsFallbackExtensions : UnixFallbackExtensions)
        {
            var candidate = commandPath + ext;
            attempted.Add(candidate);
            if (File.Exists(candidate))
                return new Result(candidate, attempted, true);
        }

        return new Result(null, attempted, false);
    }
}
