using Spectre.Console;
using Spectre.Console.Cli;

namespace MagnaFlow.WorkerController.Commands;

/// <summary>
/// The command-line surface, kept out of Program.cs so its parsing contract is testable: unknown
/// options and arguments are errors, and every usage error names itself on stderr, prints usage
/// and exits 2 (docs/specs/concepts/design.md "Shared conventions"). `--help` exits 0. The help
/// text comes from the command classes' [Description] attributes.
/// </summary>
public static class WorkerCli
{
    public const string Usage =
        """
        usage: mf-worker run <cmd-id> [--project <path>]
               mf-worker next [--project <path>]
               mf-worker run-all [--project <path>]
               mf-worker status [--project <path>]
               mf-worker --help
        """;

    public static Task<int> RunAsync(string[] args, TextWriter? stderr = null, IAnsiConsole? console = null)
    {
        var errors = stderr ?? Console.Error;
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("mf-worker");
            if (console is not null)
                config.ConfigureConsole(console);
            config.UseStrictParsing();
            config.SetExceptionHandler((ex, _) =>
            {
                errors.WriteLine($"mf-worker: {ex.Message}");
                if (ex is not CommandAppException)
                    return -1; // a crash, not a usage error: unchanged from Spectre's own default
                errors.WriteLine(Usage);
                return ExitCodes.UsageError;
            });
            config.AddCommand<RunCommand>("run")
                .WithExample("run", "0001-project-setup");
            config.AddCommand<NextCommand>("next");
            config.AddCommand<RunAllCommand>("run-all");
            config.AddCommand<StatusCommand>("status");
        });
        return app.RunAsync(args);
    }
}
