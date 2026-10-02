using System.Diagnostics;
using MagnaFlow.MfWatch.Polling;

namespace MagnaFlow.MfWatch.Tests;

/// <summary>The "Check now" wake signal (Polling/WakeFile.cs), over a real temp project dir — the
/// file system *is* the mechanism here, so there is nothing to fake.</summary>
public class WakeFileTests
{
    private static void Touch(string projectRoot)
    {
        var path = WakeFile.PathFor(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    [Fact]
    public void TryConsume_is_false_when_no_wake_file_exists()
    {
        using var project = new TempProject();
        Assert.False(WakeFile.TryConsume(project.Root));
    }

    [Fact]
    public void TryConsume_detects_the_wake_file_and_deletes_it()
    {
        using var project = new TempProject();
        Touch(project.Root);

        Assert.True(WakeFile.TryConsume(project.Root));
        Assert.False(File.Exists(WakeFile.PathFor(project.Root)));
    }

    [Fact]
    public void The_wake_fires_exactly_once()
    {
        using var project = new TempProject();
        Touch(project.Root);

        Assert.True(WakeFile.TryConsume(project.Root));
        Assert.False(WakeFile.TryConsume(project.Root));
    }

    [Fact]
    public async Task SleepAsync_returns_early_when_a_wake_file_appears()
    {
        using var project = new TempProject();
        var slice = TimeSpan.FromMilliseconds(20);
        var sleep = WakeFile.SleepAsync(TimeSpan.FromSeconds(30), project.Root, default, slice);

        Touch(project.Root);
        var stopwatch = Stopwatch.StartNew();
        Assert.True(await sleep);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"woke after {stopwatch.Elapsed}");
        Assert.False(File.Exists(WakeFile.PathFor(project.Root)));
    }

    [Fact]
    public async Task SleepAsync_returns_false_when_the_interval_just_elapses()
    {
        using var project = new TempProject();
        Assert.False(await WakeFile.SleepAsync(
            TimeSpan.FromMilliseconds(30), project.Root, default, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public async Task SleepAsync_honours_cancellation()
    {
        using var project = new TempProject();
        using var cts = new CancellationTokenSource();
        var sleep = WakeFile.SleepAsync(
            TimeSpan.FromSeconds(30), project.Root, cts.Token, TimeSpan.FromMilliseconds(20));

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sleep);
    }
}
