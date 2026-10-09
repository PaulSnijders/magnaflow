using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Watch;

namespace MagnaFlow.MfCockpit.Infrastructure;

/// <summary>Promote reuses the watch toggle's detached-spawn seam (IWatchProcessSpawner): it is the
/// cockpit's one way to start a process it does not wait for.</summary>
public sealed class MfRunClient(IProcessRunner processRunner, RunClientConfig config, IWatchProcessSpawner detachedSpawner) : IRunClient
{
    public Task<RunActionResult> StatusAsync(string projectRoot, CancellationToken cancellationToken = default) =>
        RunAsync(projectRoot, "status", service: null, extraArgs: ["--json"], cancellationToken);

    public Task<RunActionResult> StartAsync(string projectRoot, string? service, CancellationToken cancellationToken = default) =>
        RunAsync(projectRoot, "start", service, [], cancellationToken);

    public Task<RunActionResult> StopAsync(string projectRoot, string? service, CancellationToken cancellationToken = default) =>
        RunAsync(projectRoot, "stop", service, [], cancellationToken);

    public Task<RunActionResult> RestartAsync(string projectRoot, string? service, CancellationToken cancellationToken = default) =>
        RunAsync(projectRoot, "restart", service, [], cancellationToken);

    public int StartPromote(string projectRoot) =>
        detachedSpawner.Start(config.Command, ["promote", "--project", projectRoot], projectRoot);

    private async Task<RunActionResult> RunAsync(
        string projectRoot, string verb, string? service, IReadOnlyList<string> extraArgs, CancellationToken cancellationToken)
    {
        var args = new List<string> { verb };
        if (service is not null)
            args.Add(service);
        args.Add("--project");
        args.Add(projectRoot);
        args.AddRange(extraArgs);

        var result = await processRunner.RunExecutableAsync(
            config.Command, args, projectRoot, timeout: config.Timeout, cancellationToken: cancellationToken);

        var output = string.Join('\n', new[] { result.StdOut.Trim(), result.StdErr.Trim() }.Where(s => s.Length > 0));
        return new RunActionResult(result.ExitCode, output, result.TimedOut);
    }
}
