using System.Text;

namespace MagnaFlow.MfWatch.Runtime;

/// <summary>Two layers, both plain text (ontwerp-v0.1.md "Observability"): console output while
/// running, and a rolling append-only log file next to the target project's own evidence.</summary>
public sealed class Logger : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();

    public Logger(string? logFilePath)
    {
        if (logFilePath is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        _writer = new StreamWriter(
            new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        { AutoFlush = true };
    }

    public void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss} {message}";
        lock (_gate)
        {
            Console.WriteLine(line);
            _writer?.WriteLine(line);
        }
    }

    public void Dispose() => _writer?.Dispose();
}
