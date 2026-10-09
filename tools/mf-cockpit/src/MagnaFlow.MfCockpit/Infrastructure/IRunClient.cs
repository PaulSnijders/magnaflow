namespace MagnaFlow.MfCockpit.Infrastructure;

public sealed record RunActionResult(int ExitCode, string Output, bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// The Run card's only door to the outside world (ontwerp-v0.3.md "The Run card"): every status
/// read and every start/stop/restart button is a spawn of tools/mf-run, never process logic the
/// cockpit owns itself — no library reference to mf-run either, same decoupling every tool here
/// keeps. `service` null means "all services", mirroring mf-run's own CLI (no service name = all,
/// in config order).
/// </summary>
public interface IRunClient
{
    /// <summary>`mf-run status --json [--project &lt;root&gt;]` — Output is mf-run's raw stdout+stderr,
    /// expected to be a JSON array on success; the caller (RunEndpoints) is responsible for parsing it.</summary>
    Task<RunActionResult> StatusAsync(string projectRoot, CancellationToken cancellationToken = default);

    Task<RunActionResult> StartAsync(string projectRoot, string? service, CancellationToken cancellationToken = default);

    Task<RunActionResult> StopAsync(string projectRoot, string? service, CancellationToken cancellationToken = default);

    Task<RunActionResult> RestartAsync(string projectRoot, string? service, CancellationToken cancellationToken = default);

    /// <summary>`mf-run promote --project &lt;root&gt;`, spawned detached and not waited for: a publish
    /// takes minutes, far past the cockpit's mf-run timeout. Its outcome is read back through
    /// status (state.yml). Returns the PID; throws when the spawn itself fails.</summary>
    int StartPromote(string projectRoot);
}
