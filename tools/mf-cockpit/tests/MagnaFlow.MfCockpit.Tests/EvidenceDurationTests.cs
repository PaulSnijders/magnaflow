using MagnaFlow.MfCockpit.Evidence;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>EvidenceReader.DurationSeconds: claude.log's first header (local time) to the last
/// evidence write, or to the clock while `running`.</summary>
public class EvidenceDurationTests
{
    private const string Id = "0005-fix-lava";

    private static string Header(string label, DateTime local) => $"=== {label} — {local:yyyy-MM-ddTHH:mm:ss} ===";

    private static void TouchUtc(string path, DateTimeOffset utc) => File.SetLastWriteTimeUtc(path, utc.UtcDateTime);

    [Theory]
    [InlineData("plan")]
    [InlineData("attempt 1/3")]
    public void Header_to_last_write(string label)
    {
        using var project = new TempProject();
        var end = new DateTimeOffset(2026, 9, 10, 12, 30, 0, TimeSpan.Zero);
        var log = project.WriteEvidence(Id, "claude.log", Header(label, end.AddSeconds(-307).LocalDateTime) + "\nagent output\n");
        TouchUtc(log, end);

        Assert.Equal(307, EvidenceReader.DurationSeconds(project.Root, Id, running: false, new FakeClock()));
    }

    [Fact]
    public void Last_write_is_the_latest_of_all_evidence_logs()
    {
        using var project = new TempProject();
        var end = new DateTimeOffset(2026, 9, 10, 12, 30, 0, TimeSpan.Zero);
        var log = project.WriteEvidence(Id, "claude.log", Header("plan", end.AddSeconds(-100).LocalDateTime) + "\n");
        TouchUtc(log, end.AddSeconds(-50));
        TouchUtc(project.WriteEvidence(Id, "test.log", "ok\n"), end);

        Assert.Equal(100, EvidenceReader.DurationSeconds(project.Root, Id, running: false, new FakeClock()));
    }

    [Fact]
    public void Running_measures_to_the_clock()
    {
        using var project = new TempProject();
        var clock = new FakeClock();
        var log = project.WriteEvidence(Id, "claude.log", Header("plan", clock.UtcNow.AddSeconds(-23).LocalDateTime) + "\n");
        TouchUtc(log, clock.UtcNow.AddSeconds(-20));

        Assert.Equal(23, EvidenceReader.DurationSeconds(project.Root, Id, running: true, clock));

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        Assert.Equal(323, EvidenceReader.DurationSeconds(project.Root, Id, running: true, clock));
    }

    [Fact]
    public void No_log_is_null()
    {
        using var project = new TempProject();

        Assert.Null(EvidenceReader.DurationSeconds(project.Root, Id, running: false, new FakeClock()));
    }

    [Theory]
    [InlineData("agent output without a header")]
    [InlineData("=== plan — not-a-date ===")]
    [InlineData("")]
    public void Garbage_first_line_is_null(string firstLine)
    {
        using var project = new TempProject();
        project.WriteEvidence(Id, "claude.log", firstLine + "\n" + Header("plan", DateTime.Now) + "\n");

        Assert.Null(EvidenceReader.DurationSeconds(project.Root, Id, running: false, new FakeClock()));
    }
}
