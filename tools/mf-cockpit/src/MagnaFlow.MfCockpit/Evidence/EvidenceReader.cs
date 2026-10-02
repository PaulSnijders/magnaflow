using System.Globalization;
using System.Text.RegularExpressions;
using MagnaFlow.MfCockpit.Infrastructure;
using YamlDotNet.Serialization;

namespace MagnaFlow.MfCockpit.Evidence;

public sealed record CommandEvidence(TailResult ClaudeLog, TailResult BuildLog, TailResult TestLog, string? SessionId);

/// <summary>
/// Reads .magnaflow/&lt;NNNN-name&gt;/ — machine runtime evidence, tail-only (docs/mf-spec/system.md,
/// docs/fase2-worker-controller/v0.2-completion-notes.md). Read-only: the cockpit never writes
/// here (ontwerp-v0.1.md "Invariant: buttons, not an actor" — "never touches .magnaflow/").
/// </summary>
public static class EvidenceReader
{
    // AttemptLogWriter's header, always claude.log's first line: "=== plan — 2026-09-10T14:03:22 ==="
    // (older logs: "=== attempt 1/3 — ... ==="). The timestamp is local time, no offset.
    private static readonly Regex LogHeader = new(@"^=== .+ — (?<ts>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}) ===$", RegexOptions.Compiled);

    public static string EvidenceDirectory(string projectRoot, string id) =>
        Path.Combine(projectRoot, ".magnaflow", id);

    public static CommandEvidence Read(string projectRoot, string id, int maxLines = TailReader.DefaultMaxLines)
    {
        var dir = EvidenceDirectory(projectRoot, id);
        return new CommandEvidence(
            TailReader.Read(Path.Combine(dir, "claude.log"), maxLines),
            TailReader.Read(Path.Combine(dir, "build.log"), maxLines),
            TailReader.Read(Path.Combine(dir, "test.log"), maxLines),
            ReadSessionId(dir));
    }

    /// <summary>The recorded agent session id for one command, or null when it never ran (or the
    /// agent reported none) — the follow-up feature's `resume:` source (v0.2).</summary>
    public static string? ReadSessionId(string projectRoot, string id) => ReadSessionId(EvidenceDirectory(projectRoot, id));

    /// <summary>Most recent write among a command's own evidence files (claude/build/test.log),
    /// or null when none exist — used for stale-`running` detection.</summary>
    public static DateTimeOffset? LastActivityUtc(string projectRoot, string id)
    {
        var dir = EvidenceDirectory(projectRoot, id);
        if (!Directory.Exists(dir))
            return null;

        DateTimeOffset? latest = null;
        foreach (var name in new[] { "claude.log", "build.log", "test.log" })
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
                continue;
            var written = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            if (latest is null || written > latest)
                latest = written;
        }
        return latest;
    }

    /// <summary>How long a command took: claude.log's first header to the last evidence write —
    /// or to now while it is still `running`. Null when there is no claude.log or its first line
    /// is not a header. Reads only that first line: this runs for every lane command on every
    /// refresh and the log can be megabytes (same reasoning as RstSummary).</summary>
    public static int? DurationSeconds(string projectRoot, string id, bool running, IClock clock)
    {
        var start = ReadStartLocal(Path.Combine(EvidenceDirectory(projectRoot, id), "claude.log"));
        if (start is null)
            return null;

        var end = running ? clock.UtcNow : LastActivityUtc(projectRoot, id);
        if (end is null)
            return null;

        // The header is local wall-clock time, so compare it with local time, not UTC.
        var seconds = (end.Value.LocalDateTime - start.Value).TotalSeconds;
        return (int)Math.Max(0, seconds);
    }

    private static DateTime? ReadStartLocal(string claudeLog)
    {
        if (!File.Exists(claudeLog))
            return null;
        try
        {
            using var reader = new StreamReader(claudeLog);
            var match = LogHeader.Match(reader.ReadLine() ?? "");
            return match.Success
                && DateTime.TryParseExact(match.Groups["ts"].Value, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                ? start
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? ReadSessionId(string evidenceDirectory)
    {
        var path = Path.Combine(evidenceDirectory, "session.yml");
        if (!File.Exists(path))
            return null;
        try
        {
            var yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build()
                .Deserialize<Dictionary<string, string?>>(File.ReadAllText(path));
            return yaml.TryGetValue("session", out var session) && !string.IsNullOrWhiteSpace(session) ? session : null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }
}
