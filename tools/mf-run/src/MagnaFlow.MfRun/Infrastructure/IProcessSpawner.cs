namespace MagnaFlow.MfRun.Infrastructure;

/// <summary>A currently-running OS process, as observed right now — the PID plus its start time
/// (the fact the PID-reuse guard compares against). Not the same shape as mf-watch/worker-controller's
/// IProcessRunner: that seam runs a command and waits for it to finish; this one starts a detached
/// process that outlives mf-run itself, and separately answers "is this PID still the same process".</summary>
public sealed record ProcessSnapshot(int Pid, DateTimeOffset StartTimeUtc);

/// <summary>
/// The single seam to the OS for process lifecycle (duplicated rather than referenced across
/// tools, per the MagnaFlow invariant of no library coupling between them). A real spawn survives
/// the spawning process exiting — stdout/stderr are redirected at the shell level into a log file,
/// not piped through mf-run's own process, so nothing breaks when mf-run itself exits right after
/// the ~2s liveness check.
/// </summary>
public interface IProcessSpawner
{
    /// <summary>Starts <paramref name="executable"/> detached, with stdout+stderr merged and
    /// redirected (truncating) into <paramref name="logPath"/>. Returns the PID of the spawned
    /// process tree's root.</summary>
    int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, string logPath);

    /// <summary>Null if no process with this PID currently exists (already exited, or never did).</summary>
    ProcessSnapshot? GetProcess(int pid);

    /// <summary>Kills the process and its entire descendant tree. A PID that no longer exists is
    /// not an error — killing something already gone is the idempotent, expected case.</summary>
    void KillTree(int pid);
}
