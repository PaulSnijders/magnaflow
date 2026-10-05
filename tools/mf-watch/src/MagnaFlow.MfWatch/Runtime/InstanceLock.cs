using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MagnaFlow.MfWatch.Runtime;

/// <summary>
/// Never two mf-watch instances on the same working copy (ontwerp-v0.1.md "Guards"). An
/// exclusive-for-writers file handle is the lock itself (works the same for the daemon loop and a
/// single --once invocation, e.g. from an OS scheduler racing a still-running daemon) — no
/// separate PID-liveness check needed for TryAcquire itself: the OS releases the write lock the
/// moment the process dies, however it died.
///
/// The share mode is per OS. On Windows it is FileShare.Read: the share mode itself is the lock (a
/// second TryAcquire needs write access, which FileShare.Read doesn't grant a second handle), and a
/// read-only opener — mf-cockpit's MfWatchLockFile — can still see who holds it. On Unix .NET turns
/// any share mode other than None into a *shared* advisory flock, so a second watcher acquired the
/// lock too (verified on Linux, cmd 0019); FileShare.None takes an exclusive flock instead. flock is
/// advisory, so plain readers (cat) still read the file there; only another .NET FileStream, which
/// takes a shared flock of its own, is refused — and the cockpit reads the content on Windows only.
/// Content is two lines, PID
/// then start time (round-trip ISO-8601) — same shape as mf-run's PidFile — so an external reader
/// (mf-cockpit's watch toggle) can apply the same PID-reuse guard mf-run does before treating a
/// stale leftover file as "still running".
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;

    private InstanceLock(FileStream stream, string path)
    {
        _stream = stream;
        _path = path;
    }

    private static FileShare ShareMode => OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None;

    public static string LockPath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "mf-watch.lock");

    /// <summary>Returns null when another instance already holds the lock.</summary>
    public static InstanceLock? TryAcquire(string projectRoot)
    {
        var path = LockPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, ShareMode);
            stream.SetLength(0);
            var startTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            var content = $"{Environment.ProcessId}\n{startTime.ToString("o", CultureInfo.InvariantCulture)}\n";
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            return new InstanceLock(stream, path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        try { File.Delete(_path); } catch { /* best effort; a lingering empty file is harmless */ }
    }
}
