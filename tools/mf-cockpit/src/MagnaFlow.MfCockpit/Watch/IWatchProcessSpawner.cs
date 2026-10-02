using System.Diagnostics;

namespace MagnaFlow.MfCockpit.Watch;

public sealed record WatchProcessSnapshot(int Pid, DateTimeOffset StartTimeUtc);

/// <summary>The Windows watch toggle's only door to the OS (Watch/WindowsWatchControl.cs) — spawn
/// mf-watch detached and later confirm/kill it by PID, same seam shape as mf-run's own
/// IProcessSpawner, duplicated rather than referenced (no library coupling between tools). Unlike
/// mf-run's spawner, no log-file redirection: mf-watch already writes its own diary to
/// .magnaflow/mf-watch.log once it starts, so there's nothing this seam needs to capture.</summary>
public interface IWatchProcessSpawner
{
    /// <summary>Starts <paramref name="executable"/> detached and returns its PID.</summary>
    int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory);

    /// <summary>Null if no process with this PID currently exists.</summary>
    WatchProcessSnapshot? GetProcess(int pid);

    /// <summary>Killing something already gone is not an error — idempotent by design.</summary>
    void Kill(int pid);
}

public sealed class SystemWatchProcessSpawner : IWatchProcessSpawner
{
    public int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };
        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start '{executable}'");
        return process.Id;
    }

    public WatchProcessSnapshot? GetProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return new WatchProcessSnapshot(pid, process.StartTime.ToUniversalTime());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public void Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // already gone — the idempotent, expected outcome
        }
    }
}
