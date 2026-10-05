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
    public async Task SecondAcquireFromAnotherProcessIsRefusedWhileHeld()
    {
        // The real binary in a child process: a cross-process check that cannot be hidden by
        // in-process lock semantics. Exit 3 = "another instance holds the lock".
        using var project = new TempProject();
        using var @lock = InstanceLock.TryAcquire(project.Root);
        Assert.NotNull(@lock);

        var watchDll = Path.Combine(AppContext.BaseDirectory, "MagnaFlow.MfWatch.dll");
        var missingConfig = Path.Combine(project.Root, "no-such-magnaflow.yml"); // missing file = defaults
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { watchDll, "--once", "--project", project.Root, "--config", missingConfig })
            psi.ArgumentList.Add(arg);

        using var child = Process.Start(psi)!;
        _ = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await child.WaitForExitAsync(timeout.Token);
        Assert.True(child.ExitCode == 3, $"expected exit 3, got {child.ExitCode}: {await stderr}");
    }

    [Fact]
    public void LockFileIsReadableByAnotherReaderWhileHeld()
    {
        using var project = new TempProject();
        using var @lock = InstanceLock.TryAcquire(project.Root);
        Assert.NotNull(@lock);

        var lines = ReadWhileHeld(InstanceLock.LockPath(project.Root)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal(Environment.ProcessId, int.Parse(lines[0], CultureInfo.InvariantCulture));
        var startTime = DateTimeOffset.Parse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(Process.GetCurrentProcess().StartTime.ToUniversalTime(), startTime.UtcDateTime, TimeSpan.FromSeconds(2));
    }

    // Windows: any .NET reader works (the holder shares Read). Unix: the holder's flock is
    // exclusive, so a .NET FileStream (which takes a shared flock) is refused, but a plain reader
    // that takes no lock — cat — still reads it; flock is advisory.
    private static string ReadWhileHeld(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var streamReader = new StreamReader(reader);
            return streamReader.ReadToEnd();
        }

        var psi = new ProcessStartInfo("cat") { RedirectStandardOutput = true };
        psi.ArgumentList.Add(path);
        using var cat = Process.Start(psi)!;
        var content = cat.StandardOutput.ReadToEnd();
        cat.WaitForExit();
        Assert.Equal(0, cat.ExitCode);
        return content;
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
