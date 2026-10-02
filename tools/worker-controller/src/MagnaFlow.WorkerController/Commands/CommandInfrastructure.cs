using System.ComponentModel;
using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Execution;
using MagnaFlow.WorkerController.Infrastructure;
using MagnaFlow.WorkerController.Prompts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MagnaFlow.WorkerController.Commands;

public class ProjectSettings : CommandSettings
{
    [CommandOption("--project <PATH>")]
    [Description("Target project root (must contain docs/prompts/ and .magnaflow/). Defaults to the current directory.")]
    public string? Project { get; set; }

    public string ResolveRoot() => Path.GetFullPath(Project ?? Directory.GetCurrentDirectory());
}

internal static class CommandInfrastructure
{
    /// <summary>Diagnostics go to stderr; stdout is display only (contracts/cli.md).</summary>
    public static void Error(string message) => Console.Error.WriteLine($"mf-worker: {message}");

    public static void Info(string message) => AnsiConsole.MarkupLineInterpolated($"[grey]{message}[/]");

    public static (ProjectConfig? Config, int ExitCode) LoadConfigOrFail(string projectRoot)
    {
        var (cfg, error) = ProjectConfig.Load(projectRoot);
        if (cfg is null)
        {
            Error(error!);
            return (null, ExitCodes.UsageError);
        }
        return (cfg, ExitCodes.Success);
    }

    /// <summary>
    /// The queue must never be read from a task's work branch: its .magnaflow/ content is a
    /// frozen mid-run snapshot, not the live queue (spec FR-006a). Returns an exit code to
    /// return immediately, or null when it is safe to proceed.
    /// </summary>
    public static async Task<int?> RefuseIfOnWorkBranchAsync(string projectRoot)
    {
        var git = new GitClient(new CliWrapProcessRunner(), projectRoot);
        if (!await git.IsAvailableAsync())
        {
            Error("git is not available on this machine");
            return ExitCodes.EnvironmentError;
        }
        var current = await git.GetCurrentBranchAsync();
        var owner = PromptScanner.Scan(projectRoot)
            .FirstOrDefault(c => c.Cmd?.Branch is not null && c.Cmd.Branch == current);
        if (owner is null)
            return null;

        var home = await git.GetDefaultBranchAsync() ?? "<your orchestration branch>";
        Error($"you are on '{current}', the work branch of command {owner.Id}");
        Error("command state on a work branch is a frozen mid-run snapshot - the queue here is not the live queue,");
        Error("and running from here would pollute the work branch with orchestration commits");
        Error($"switch back to your orchestration branch first:  git checkout {home}");
        return ExitCodes.PreconditionRefused;
    }

    public static TaskRunner BuildTaskRunner(ProjectConfig config, string projectRoot)
    {
        var processes = new CliWrapProcessRunner();
        var git = new GitClient(processes, projectRoot);
        var agent = new ClaudeCodeRunner(processes, config);
        return new TaskRunner(config, git, agent, processes, projectRoot, Info, Error);
    }

    /// <summary>Git failures mid-run leave the committed running state behind by design (spec: stuck-running edge case).</summary>
    public static async Task<int> GuardEnvironmentAsync(Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (GitException ex)
        {
            Error($"git operation failed: {ex.Message}");
            Error("the task folder and git history show how far the run got; inspect and reset the task status if needed");
            return ExitCodes.EnvironmentError;
        }
    }
}
