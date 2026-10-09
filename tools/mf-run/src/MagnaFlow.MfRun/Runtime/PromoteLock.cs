using MagnaFlow.MfRun.Config;

namespace MagnaFlow.MfRun.Runtime;

/// <summary>
/// .magnaflow/stable/promote.lock — an OS-level exclusive lock, not a marker file, so a killed
/// promote frees it (docs/specs/run/mf-run.md#stable-instance). Same mechanism as mf-watch's
/// InstanceLock: FileShare.None is a no-share handle on Windows and an exclusive flock on Unix. The
/// file itself is never deleted: deleting it would let a second promote lock a fresh inode while the
/// first still holds the old one, and an empty leftover file is harmless.
/// </summary>
public sealed class PromoteLock : IDisposable
{
    private readonly FileStream _stream;

    private PromoteLock(FileStream stream) => _stream = stream;

    public static string LockPath(string projectRoot) => Path.Combine(StableConfig.StableDirectory(projectRoot), "promote.lock");

    /// <summary>Null when another promote holds the lock.</summary>
    public static PromoteLock? TryAcquire(string projectRoot)
    {
        var path = LockPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new PromoteLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Whether a promote holds the lock right now: try to take it and let go at once.</summary>
    public static bool IsHeld(string projectRoot)
    {
        var path = LockPath(projectRoot);
        if (!File.Exists(path))
            return false;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void Dispose() => _stream.Dispose();
}
