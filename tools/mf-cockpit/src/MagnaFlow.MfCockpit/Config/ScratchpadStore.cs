using System.Security.Cryptography;
using System.Text;
using MagnaFlow.MfCockpit.Projects;

namespace MagnaFlow.MfCockpit.Config;

/// <summary>
/// The one store the cockpit deliberately keeps OUTSIDE git (ontwerp-v0.5.md item 8): a per-project
/// notepad living next to the magnaflow.yml the cockpit actually loaded — the directory of the
/// fase-7 resolved <see cref="CockpitConfig.ConfigPath"/>, even when that file doesn't exist on disk
/// (running on defaults → the next-to-binary primary path). One <c>&lt;project&gt;.md</c> per
/// project, filename through write #6's <see cref="DirNameSanitizer"/>. It must never land in a
/// working copy — an untracked file there would dirty the very tree the worker guards. Permissible
/// as an out-of-git store because it is plain text holding notes, not system state, that no other
/// tool ever reads. Concurrency is handled entirely by the hash check on save (no FileSystemWatcher
/// fires for a file outside every watched directory).
/// </summary>
public sealed class ScratchpadStore(CockpitConfig config)
{
    public const int MaxContentBytes = 256 * 1024;

    /// <summary>scratchpad/ next to the loaded magnaflow.yml (ontwerp-v0.5.md item 8).</summary>
    public string Directory =>
        Path.Combine(Path.GetDirectoryName(config.ConfigPath) ?? AppContext.BaseDirectory, "scratchpad");

    /// <summary>The file a given project's notes live in. Name is sanitized like a new-project
    /// dirname; a reserved (CON, NUL, …) or empty result is made safe with a stable hashed fallback,
    /// so no project name can ever escape the scratchpad directory or hit a Windows device name.</summary>
    public string PathFor(string projectName)
    {
        var safe = DirNameSanitizer.Sanitize(projectName);
        if (string.IsNullOrEmpty(safe) || DirNameSanitizer.IsReservedName(safe))
            safe = "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectName)))[..12].ToLowerInvariant();
        return Path.Combine(Directory, safe + ".md");
    }

    public static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public sealed record Snapshot(string Content, string Hash, string? SavedAt);

    public Snapshot Read(string projectName)
    {
        var path = PathFor(projectName);
        if (!File.Exists(path))
            return new Snapshot("", ComputeHash(""), null);
        var content = File.ReadAllText(path);
        var savedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToString("o");
        return new Snapshot(content, ComputeHash(content), savedAt);
    }

    public enum SaveOutcome { Ok, Conflict, TooLarge }

    public sealed record SaveResult(SaveOutcome Outcome, string? Hash, string? SavedAt);

    /// <summary>Optimistic-concurrency save (ontwerp-v0.5.md item 8): baseHash must match the file's
    /// current hash (409 on drift), content is capped at 256 KB (like the config PUT). No path
    /// parameter is ever involved — the filename is derived from the project name server-side, so
    /// there is nothing to escape.</summary>
    public SaveResult Save(string projectName, string content, string baseHash)
    {
        var path = PathFor(projectName);
        var current = File.Exists(path) ? File.ReadAllText(path) : "";
        if (!string.Equals(ComputeHash(current), baseHash, StringComparison.Ordinal))
            return new SaveResult(SaveOutcome.Conflict, null, null);

        if (Encoding.UTF8.GetByteCount(content) > MaxContentBytes)
            return new SaveResult(SaveOutcome.TooLarge, null, null);

        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(path, content);
        var savedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToString("o");
        return new SaveResult(SaveOutcome.Ok, ComputeHash(content), savedAt);
    }
}
