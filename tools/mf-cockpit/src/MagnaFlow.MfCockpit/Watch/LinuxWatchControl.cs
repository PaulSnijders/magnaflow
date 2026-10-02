using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Watch;

/// <summary>Linux already has real per-project mf-watch supervision: the mf-watch@.service
/// systemd template unit (tools/install/install.sh), one instance enabled per watched project
/// path. This toggle is a thin shell-out to systemd-escape + systemctl --user — no process/PID
/// logic of its own, same "shell out, own nothing" posture as MfRunClient.</summary>
public sealed class LinuxWatchControl(IProcessRunner processRunner) : IWatchControl
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<bool> IsRunningAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var unit = await UnitNameAsync(projectRoot, cancellationToken);
        var result = await processRunner.RunExecutableAsync(
            "systemctl", ["--user", "is-active", "--quiet", unit], projectRoot, timeout: Timeout, cancellationToken: cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task<WatchControlResult> StartAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var unit = await UnitNameAsync(projectRoot, cancellationToken);
        var result = await processRunner.RunExecutableAsync(
            "systemctl", ["--user", "enable", "--now", unit], projectRoot, timeout: Timeout, cancellationToken: cancellationToken);
        return ToResult(result);
    }

    public async Task<WatchControlResult> StopAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var unit = await UnitNameAsync(projectRoot, cancellationToken);
        var result = await processRunner.RunExecutableAsync(
            "systemctl", ["--user", "disable", "--now", unit], projectRoot, timeout: Timeout, cancellationToken: cancellationToken);
        return ToResult(result);
    }

    private async Task<string> UnitNameAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunExecutableAsync(
            "systemd-escape", [projectRoot], projectRoot, timeout: Timeout, cancellationToken: cancellationToken);
        var escaped = result.StdOut.Trim();
        return $"mf-watch@{escaped}.service";
    }

    private static WatchControlResult ToResult(ProcessResult result) =>
        result.Succeeded
            ? new WatchControlResult(true, null)
            : new WatchControlResult(false, string.Join('\n', new[] { result.StdOut.Trim(), result.StdErr.Trim() }.Where(s => s.Length > 0)));
}
