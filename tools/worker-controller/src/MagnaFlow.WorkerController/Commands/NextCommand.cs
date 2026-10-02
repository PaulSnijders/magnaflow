using System.ComponentModel;
using MagnaFlow.WorkerController.Execution;
using MagnaFlow.WorkerController.Prompts;
using Spectre.Console.Cli;

namespace MagnaFlow.WorkerController.Commands;

[Description("Execute the first ready command in ID order.")]
public sealed class NextCommand : AsyncCommand<ProjectSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, ProjectSettings settings, CancellationToken cancellationToken) =>
        CommandInfrastructure.GuardEnvironmentAsync(async () =>
        {
            var root = settings.ResolveRoot();
            var (config, exit) = CommandInfrastructure.LoadConfigOrFail(root);
            if (config is null) return exit;
            if (await CommandInfrastructure.RefuseIfOnWorkBranchAsync(root) is { } refused) return refused;

            var next = PromptScanner.FirstReady(PromptScanner.Scan(root));
            if (next is null)
            {
                CommandInfrastructure.Info("nothing ready");
                return ExitCodes.Success; // an empty queue is a normal outcome for a one-shot tool
            }

            var runner = CommandInfrastructure.BuildTaskRunner(config, root);
            return await runner.RunAsync(next, new AgentSessionState());
        });
}
