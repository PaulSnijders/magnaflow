using System.ComponentModel;
using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Infrastructure;
using MagnaFlow.WorkerController.Prompts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MagnaFlow.WorkerController.Commands;

[Description("Show a read-only overview of all commands. Modifies nothing.")]
public sealed class StatusCommand : AsyncCommand<ProjectSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ProjectSettings settings, CancellationToken cancellationToken)
    {
        var root = settings.ResolveRoot();

        // status must work without a valid config (FR-016): config only supplies the max-attempts default.
        var (config, _) = ProjectConfig.Load(root);
        var commands = PromptScanner.Scan(root);
        var rows = StatusOverview.BuildRows(commands, config?.MaxAttempts);

        if (rows.Count == 0)
        {
            CommandInfrastructure.Info($"no commands found under {PromptScanner.PromptsRoot(root)}");
            return ExitCodes.Success;
        }

        var table = new Table().AddColumns("Command", "Title", "Status", "Attempts");
        foreach (var row in rows)
        {
            var status = row.Status switch
            {
                "done" => "[green]done[/]",
                "aborted" => "[red]aborted[/]",
                "running" => "[yellow]running[/]",
                "questions" => "[cyan]questions[/]",
                "!" => "[red]![/]",
                _ => row.Status,
            };
            table.AddRow(
                Markup.Escape(row.Id),
                row.Warning ? $"[yellow]{Markup.Escape(row.Title)}[/]" : Markup.Escape(row.Title),
                status,
                Markup.Escape(row.Attempts));
        }
        AnsiConsole.Write(table);

        await WarnIfOnWorkBranchAsync(root, commands);
        return ExitCodes.Success;
    }

    /// <summary>Best effort: status stays usable without git, but a frozen-snapshot view deserves a warning (FR-006a).</summary>
    private static async Task WarnIfOnWorkBranchAsync(string root, IReadOnlyList<ScannedCommand> commands)
    {
        try
        {
            var git = new GitClient(new CliWrapProcessRunner(), root);
            if (!await git.IsAvailableAsync())
                return;
            var current = await git.GetCurrentBranchAsync();
            var owner = commands.FirstOrDefault(c => c.Cmd?.Branch is not null && c.Cmd.Branch == current);
            if (owner is not null)
                CommandInfrastructure.Error(
                    $"note: you are on '{current}', command {owner.Id}'s work branch — this overview is that branch's frozen snapshot, not the live queue");
        }
        catch (GitException)
        {
            // not a git repo / detached HEAD: the table above is still valid, just skip the hint
        }
    }
}
