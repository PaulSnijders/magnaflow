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
/// Opened with FileShare.Read (not FileShare.None) so the lock file is externally readable while
/// held — a second TryAcquire still fails (it needs write access, which FileShare.Read doesn't
/// grant a second handle), but a read-only opener can see who holds it. Content is two lines, PID
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

    public static string LockPath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "mf-watch.lock");

    /// <summary>Returns null when another instance already holds the lock.</summary>
    public static InstanceLock? TryAcquire(string projectRoot)
    {
        var path = LockPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
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
