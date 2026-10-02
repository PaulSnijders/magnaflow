using MagnaFlow.MfCockpit.Config;

namespace MagnaFlow.MfCockpit.Infrastructure;

public sealed class MfRunClient(IProcessRunner processRunner, RunClientConfig config) : IRunClient
{
    public Task<RunActionResult> StatusAsync(string projectRoot, CancellationToken cancellationToken = default) =>
        RunAsync(projectRoot, "status", service: null, extraArgs: ["--json"], cancellationToken);

    public Task<RunActionResult> StartAsync(string projectRoot, string? service, CancellationToken cancellationToken = default) =>
        RunAsync(projectRoot, "start", service, [], cancellationToken);

    public Task<RunActionResult> StopAsync(string projectRoot, string? service, CancellationToken cancellationToken = default) =>
        RunAsync(projectRoot, "stop", service, [], cancellationToken);

    public Task<RunActionResult> RestartAsync(string projectRoot, string? service, CancellationToken cancellationToken = default) =>
        RunAsync(projectRoot, "restart", service, [], cancellationToken);

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
