using MagnaFlow.MfRun.Runtime;

namespace MagnaFlow.MfRun.Tests;

public class PidFileTests
{
    [Fact]
    public void WriteThenReadRoundTrips()
    {
        using var project = new TempProject();
        var path = Path.Combine(project.Root, ".magnaflow", "run", "web.pid");
        var startTime = new DateTimeOffset(2026, 7, 14, 10, 30, 0, TimeSpan.Zero);

        PidFile.Write(path, 4242, startTime);
        var entry = PidFile.TryRead(path);

        Assert.NotNull(entry);
        Assert.Equal(4242, entry!.Pid);
        Assert.Equal(startTime, entry.StartTimeUtc);
    }

    [Fact]
    public void WriteIsPlainTextTwoLines()
    {
        using var project = new TempProject();
        var path = Path.Combine(project.Root, ".magnaflow", "run", "web.pid");

        PidFile.Write(path, 4242, new DateTimeOffset(2026, 7, 14, 10, 30, 0, TimeSpan.Zero));
        var lines = File.ReadAllLines(path);

        Assert.Equal("4242", lines[0]);
        Assert.StartsWith("2026-07-14T10:30:00", lines[1]);
    }

    [Fact]
    public void MissingFileReturnsNull()
    {
        var entry = PidFile.TryRead(Path.Combine(Path.GetTempPath(), "mf-run-tests", Guid.NewGuid().ToString("N") + ".pid"));

        Assert.Null(entry);
    }

    [Fact]
    public void MalformedFileReturnsNull()
    {
        using var project = new TempProject();
        var path = project.WriteFile(Path.Combine(".magnaflow", "run", "web.pid"), "not-a-pid\ngarbage\n");

        var entry = PidFile.TryRead(path);

        Assert.Null(entry);
    }

    [Fact]
    public void DeleteIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), "mf-run-tests", Guid.NewGuid().ToString("N") + ".pid");

        PidFile.Delete(path); // no throw even though the file never existed
        PidFile.Delete(path);
    }
}
