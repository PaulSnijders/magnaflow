using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Projects;

namespace MagnaFlow.MfCockpit.Config;

/// <summary>
/// The scratchpad's file drop (docs/specs/cockpit/scratchpad.md#files): a temporary transfer area
/// per project in <c>scratchpad/&lt;safe&gt;.files/</c>, next to the note and named by the same
/// sanitizer + hash fallback (<see cref="ScratchpadStore.SafeNameFor"/>). Outside every working copy,
/// read by no tool. Uploads are streamed into a hidden <c>.upload-&lt;guid&gt;.tmp</c> in the same
/// directory and renamed into place once complete, so an aborted upload never shows in the list.
/// </summary>
public sealed class ScratchpadFileStore(ScratchpadStore notes)
{
    public const long MaxFileBytes = 200L * 1024 * 1024;
    public const int MaxNameLength = 200;
    private const string TempPrefix = ".upload-";
    private const string TempSuffix = ".tmp";

    /// <summary>The per-file cap enforced while copying; settable only so tests need not write 200 MB.</summary>
    public long MaxBytes { get; init; } = MaxFileBytes;

    public sealed record Entry(string Name, long Size, string UploadedAt);

    public enum SaveOutcome { Ok, BadName, TooLarge }

    public sealed record SaveResult(SaveOutcome Outcome, Entry? Entry);

    public string DirectoryFor(string projectName) =>
        Path.Combine(notes.Directory, ScratchpadStore.SafeNameFor(projectName) + ".files");

    private static bool IsTempName(string name) =>
        name.StartsWith(TempPrefix, StringComparison.Ordinal) && name.EndsWith(TempSuffix, StringComparison.Ordinal);

    /// <summary>The one place a path parameter enters the scratchpad: last path segment only,
    /// characters invalid on Windows or Linux and control characters → <c>_</c>, trailing dots and
    /// spaces trimmed (Windows drops them), capped at 200 chars keeping the extension. Null when the
    /// result is empty, dots only, a reserved device name, or the shape of an upload temp file.</summary>
    public static string? SanitizeFileName(string? raw)
    {
        if (raw is null)
            return null;
        var cut = raw.LastIndexOfAny(['/', '\\']);
        var segment = cut >= 0 ? raw[(cut + 1)..] : raw;

        var chars = segment.Select(c => c < 0x20 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray();
        var name = new string(chars).TrimEnd('.', ' ');

        if (name.Length > MaxNameLength)
        {
            var dot = name.LastIndexOf('.');
            var ext = dot > 0 && name.Length - dot <= 20 ? name[dot..] : "";
            name = (name[..(MaxNameLength - ext.Length)] + ext).TrimEnd('.', ' ');
        }

        if (name.Length == 0 || name.All(c => c == '.'))
            return null;
        var stem = name.Split('.')[0].TrimEnd(' ');
        if (DirNameSanitizer.IsReservedName(stem))
            return null;
        if (IsTempName(name))
            return null;
        return name;
    }

    public IReadOnlyList<Entry> List(string projectName)
    {
        var dir = DirectoryFor(projectName);
        if (!System.IO.Directory.Exists(dir))
            return [];
        return new DirectoryInfo(dir).EnumerateFiles()
            .Where(f => !IsTempName(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .Select(ToEntry)
            .ToList();
    }

    private static Entry ToEntry(FileInfo f) =>
        new(f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero).ToString("o"));

    public async Task<SaveResult> SaveAsync(string projectName, string? fileName, Stream body, CancellationToken ct)
    {
        var name = SanitizeFileName(fileName);
        if (name is null)
            return new SaveResult(SaveOutcome.BadName, null);
        var dir = DirectoryFor(projectName);
        var final = PathGuard.ResolveInside(dir, name);
        if (final is null)
            return new SaveResult(SaveOutcome.BadName, null);

        System.IO.Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, TempPrefix + Guid.NewGuid().ToString("N") + TempSuffix);
        try
        {
            await using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                    {
                        await output.DisposeAsync();
                        TryDelete(tmp);
                        return new SaveResult(SaveOutcome.TooLarge, null);
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            File.Move(tmp, final, overwrite: true);
            return new SaveResult(SaveOutcome.Ok, ToEntry(new FileInfo(final)));
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>The full path of a stored file, or null when the name is not one the store would
    /// have stored (it must sanitize to itself), escapes the directory, or does not exist.</summary>
    private string? Resolve(string projectName, string? fileName)
    {
        var name = SanitizeFileName(fileName);
        if (name is null || !string.Equals(name, fileName, StringComparison.Ordinal))
            return null;
        var path = PathGuard.ResolveInside(DirectoryFor(projectName), name);
        return path is not null && File.Exists(path) ? path : null;
    }

    public Stream? OpenRead(string projectName, string? fileName)
    {
        var path = Resolve(projectName, fileName);
        if (path is null)
            return null;
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public bool Delete(string projectName, string? fileName)
    {
        var path = Resolve(projectName, fileName);
        if (path is null)
            return false;
        try
        {
            File.Delete(path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
