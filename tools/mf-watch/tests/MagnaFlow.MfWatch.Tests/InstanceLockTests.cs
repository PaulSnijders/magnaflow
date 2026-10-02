using System.Diagnostics;
using System.Globalization;
using MagnaFlow.MfWatch.Runtime;

namespace MagnaFlow.MfWatch.Tests;

public class InstanceLockTests
{
    [Fact]
    public void FirstAcquireSucceeds()
    {
        using var project = new TempProject();
        using var @lock = InstanceLock.TryAcquire(project.Root);
        Assert.NotNull(@lock);
        Assert.True(File.Exists(InstanceLock.LockPath(project.Root)));
    }

    [Fact]
    public void SecondAcquireIsRefusedWhileFirstHeld()
    {
        using var project = new TempProject();
        using var first = InstanceLock.TryAcquire(project.Root);
        Assert.NotNull(first);

        var second = InstanceLock.TryAcquire(project.Root);
        Assert.Null(second);
    }

    [Fact]
    public void AcquireSucceedsAgainAfterDispose()
    {
        using var project = new TempProject();
        var first = InstanceLock.TryAcquire(project.Root);
        Assert.NotNull(first);
        first!.Dispose();

        using var second = InstanceLock.TryAcquire(project.Root);
        Assert.NotNull(second);
    }

    [Fact]
    public void LockFileIsReadableByAnotherHandleWhileHeld()
    {
        using var project = new TempProject();
        using var @lock = InstanceLock.TryAcquire(project.Root);
        Assert.NotNull(@lock);

        using var reader = new FileStream(InstanceLock.LockPath(project.Root), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var streamReader = new StreamReader(reader);
        var lines = streamReader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal(Environment.ProcessId, int.Parse(lines[0], CultureInfo.InvariantCulture));
        var startTime = DateTimeOffset.Parse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(Process.GetCurrentProcess().StartTime.ToUniversalTime(), startTime.UtcDateTime, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SecondAcquireStillFailsAfterShareModeChange()
    {
        using var project = new TempProject();
        using var first = InstanceLock.TryAcquire(project.Root);
        Assert.NotNull(first);

        Assert.Throws<IOException>(() =>
            new FileStream(InstanceLock.LockPath(project.Root), FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }
}
