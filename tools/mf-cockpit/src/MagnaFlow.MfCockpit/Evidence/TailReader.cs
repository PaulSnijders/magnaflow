using System.Text;

namespace MagnaFlow.MfCockpit.Evidence;

public sealed record TailResult(IReadOnlyList<string> Lines, bool Truncated, bool Exists);

/// <summary>
/// Bounded tail of a log file — evidence logs can be huge (ontwerp-v0.1.md "Guards": "Log tails
/// are bounded (default last 200 lines / 64 KB)"). Reads at most MaxBytes from the end of the
/// file, never the whole thing, then keeps at most maxLines of the decoded text.
/// </summary>
public static class TailReader
{
    public const int DefaultMaxLines = 200;
    public const int MaxBytes = 64 * 1024;

    public static TailResult Read(string path, int maxLines = DefaultMaxLines)
    {
        if (!File.Exists(path))
            return new TailResult([], Truncated: false, Exists: false);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var readFromByteWindow = stream.Length > MaxBytes;
        var start = readFromByteWindow ? stream.Length - MaxBytes : 0;
        stream.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd();

        var allLines = text.Split('\n');
        var lines = allLines.Select(l => l.TrimEnd('\r')).ToList();
        // Dropping a partial first line when we started mid-file (byte window, not a line boundary).
        if (readFromByteWindow && lines.Count > 1)
            lines.RemoveAt(0);

        var truncated = readFromByteWindow || lines.Count > maxLines;
        var tail = lines.Count > maxLines ? lines[^maxLines..] : lines;
        return new TailResult(tail, truncated, Exists: true);
    }
}
