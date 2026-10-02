using MagnaFlow.WorkerController.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("mf-worker");
    config.AddCommand<RunCommand>("run")
        .WithDescription("Execute one specific task end-to-end.")
        .WithExample("run", "0001-project-setup");
    config.AddCommand<NextCommand>("next")
        .WithDescription("Execute the first pending task in folder-name order.");
    config.AddCommand<RunAllCommand>("run-all")
        .WithDescription("Execute all pending tasks one by one, then stop.");
    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show a read-only overview of all tasks.");
});
return await app.RunAsync(args);
