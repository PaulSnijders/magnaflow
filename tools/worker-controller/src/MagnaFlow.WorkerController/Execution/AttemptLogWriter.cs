using System.Text;

namespace MagnaFlow.WorkerController.Execution;

/// <summary>
/// Append-only plain-text logs per task folder (claude.log / build.log / test.log),
/// with an attempt header per attempt (contracts/file-formats.md).
/// </summary>
public sealed class AttemptLogWriter(string logPath) : IDisposable
{
    private readonly StreamWriter _writer = new(
        new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
    { AutoFlush = true };

    private readonly object _gate = new();

    public void WriteAttemptHeader(int attempt, int maxAttempts, DateTimeOffset timestamp)
    {
        lock (_gate)
            _writer.WriteLine($"=== attempt {attempt}/{maxAttempts} — {timestamp:yyyy-MM-ddTHH:mm:ss} ===");
    }

    /// <summary>Header for the plan phase, which shares this log but isn't itself an "attempt" (spec FR-011a).</summary>
    public void WritePhaseHeader(string label, DateTimeOffset timestamp)
    {
        lock (_gate)
            _writer.WriteLine($"=== {label} — {timestamp:yyyy-MM-ddTHH:mm:ss} ===");
    }

    public void WriteLine(string line)
    {
        lock (_gate)
            _writer.WriteLine(line);
    }

    public void Dispose() => _writer.Dispose();
}
