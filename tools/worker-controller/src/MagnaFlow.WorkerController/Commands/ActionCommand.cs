using System.ComponentModel;
using MagnaFlow.WorkerController.Actions;
using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MagnaFlow.WorkerController.Commands;

[Description("Run one project action from .magnaflow/actions.yml once.")]
public sealed class ActionCommand : AsyncCommand<ActionCommand.Settings>
{
    public sealed class Settings : ProjectSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("Action name: a key under actions: in .magnaflow/actions.yml.")]
        public string Name { get; set; } = string.Empty;

        [CommandOption("--trigger <TRIGGER>")]
        [Description("manual (default) or schedule; only recorded in the action log and last.yml.")]
        public string Trigger { get; set; } = "manual";

        public override ValidationResult Validate() =>
            Trigger is "manual" or "schedule"
                ? ValidationResult.Success()
                : ValidationResult.Error($"--trigger must be manual or schedule, not '{Trigger}'");
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        CommandInfrastructure.GuardEnvironmentAsync(() =>
        {
            var root = settings.ResolveRoot();
            var processes = new CliWrapProcessRunner();
            var runner = new ActionRunner(
                new GitClient(processes, root),
                processes,
                config => new ClaudeCodeRunner(processes, config),
                root,
                Console.Out.WriteLine,
                CommandInfrastructure.Error);
            return runner.RunAsync(settings.Name, settings.Trigger);
        });
}
