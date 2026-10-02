using System.ComponentModel;
using MagnaFlow.WorkerController.Execution;
using MagnaFlow.WorkerController.Prompts;
using Spectre.Console.Cli;

namespace MagnaFlow.WorkerController.Commands;

[Description("Execute one specific command end-to-end.")]
public sealed class RunCommand : AsyncCommand<RunCommand.Settings>
{
    public sealed class Settings : ProjectSettings
    {
        [CommandArgument(0, "<cmd-id>")]
        [Description("Command ID, e.g. 0001-add-export-button.")]
        public string CmdId { get; set; } = string.Empty;
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        CommandInfrastructure.GuardEnvironmentAsync(async () =>
        {
            var root = settings.ResolveRoot();
            var (config, exit) = CommandInfrastructure.LoadConfigOrFail(root);
            if (config is null) return exit;
            if (await CommandInfrastructure.RefuseIfOnWorkBranchAsync(root) is { } refused) return refused;

            var commands = PromptScanner.Scan(root);
            var scanned = PromptScanner.FindById(commands, settings.CmdId);
            if (scanned is null)
            {
                CommandInfrastructure.Error($"unknown command '{settings.CmdId}' (no matching NNNN-cmd-name.md under docs/prompts/)");
                return ExitCodes.UsageError;
            }

            var runner = CommandInfrastructure.BuildTaskRunner(config, root);
            return await runner.RunAsync(scanned, new AgentSessionState());
        });
}
