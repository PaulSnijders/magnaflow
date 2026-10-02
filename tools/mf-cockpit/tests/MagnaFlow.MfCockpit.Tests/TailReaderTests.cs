using MagnaFlow.MfCockpit.Evidence;

namespace MagnaFlow.MfCockpit.Tests;

public class TailReaderTests
{
    [Fact]
    public void Read_reports_not_exists_for_missing_file()
    {
        var result = TailReader.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.False(result.Exists);
        Assert.False(result.Truncated);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Read_returns_all_lines_when_under_the_bound()
    {
        using var project = new TempProject();
        var path = project.WriteFile("small.log", "line1\nline2\nline3\n");

        var result = TailReader.Read(path, maxLines: 200);

        Assert.True(result.Exists);
        Assert.False(result.Truncated);
        Assert.Equal(["line1", "line2", "line3", ""], result.Lines);
    }

    [Fact]
    public void Read_bounds_by_line_count()
    {
        using var project = new TempProject();
        var lines = Enumerable.Range(1, 500).Select(i => $"line{i}");
        var path = project.WriteFile("big.log", string.Join('\n', lines));

        var result = TailReader.Read(path, maxLines: 10);

        Assert.True(result.Truncated);
        Assert.Equal(10, result.Lines.Count);
        Assert.Equal("line500", result.Lines[^1]);
    }

    [Fact]
    public void Read_bounds_by_byte_window_for_huge_files()
    {
        using var project = new TempProject();
        // Each line ~20 bytes; write well past the 64KB window so only the tail survives.
        var line = new string('x', 15) + "-marker-END\n";
        var content = string.Concat(Enumerable.Repeat(line, 10_000));
        var path = project.WriteFile("huge.log", content);

        var result = TailReader.Read(path, maxLines: 5);

        Assert.True(result.Truncated);
        Assert.True(result.Lines.Count <= 5);
        Assert.All(result.Lines, l => Assert.True(l.Length == 0 || l.EndsWith("-marker-END")));
    }
}
