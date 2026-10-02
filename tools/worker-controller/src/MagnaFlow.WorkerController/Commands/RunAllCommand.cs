using System.ComponentModel;
using MagnaFlow.WorkerController.Execution;
using MagnaFlow.WorkerController.Prompts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MagnaFlow.WorkerController.Commands;

[Description("Execute all ready commands one by one, then stop.")]
public sealed class RunAllCommand : AsyncCommand<ProjectSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, ProjectSettings settings, CancellationToken cancellationToken) =>
        CommandInfrastructure.GuardEnvironmentAsync(async () =>
        {
            var root = settings.ResolveRoot();
            var (config, exit) = CommandInfrastructure.LoadConfigOrFail(root);
            if (config is null) return exit;
            if (await CommandInfrastructure.RefuseIfOnWorkBranchAsync(root) is { } refused) return refused;

            var runner = CommandInfrastructure.BuildTaskRunner(config, root);
            var (code, outcomes) = await RunAllExecutor.ExecuteAsync(
                () => PromptScanner.Scan(root),
                runner.RunAsync,
                CommandInfrastructure.Info);

            if (outcomes.Count > 0)
            {
                var table = new Table().AddColumns("Command", "Outcome");
                foreach (var outcome in outcomes)
                    table.AddRow(outcome.Id, outcome.ExitCode switch
                    {
                        ExitCodes.Success => "[green]done[/]",
                        ExitCodes.TaskFailed => "[red]aborted[/]",
                        _ => $"[yellow]error (exit {outcome.ExitCode})[/]",
                    });
                AnsiConsole.Write(table);
            }

            return code;
        });
}
