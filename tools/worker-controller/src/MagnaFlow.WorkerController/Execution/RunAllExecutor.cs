using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Execution;

/// <summary>
/// Batch loop for run-all (v0.1 FR-015/FR-017): drain ready commands in ID order, then stop.
/// Extracted from the command so the loop, aggregation, and session hand-over are unit-testable.
/// </summary>
public static class RunAllExecutor
{
    public sealed record CommandOutcome(string Id, int ExitCode);

    public static async Task<(int ExitCode, IReadOnlyList<CommandOutcome> Outcomes)> ExecuteAsync(
        Func<IReadOnlyList<ScannedCommand>> scan,
        Func<ScannedCommand, AgentSessionState, Task<int>> runCommand,
        Action<string> info)
    {
        var session = new AgentSessionState();
        var attempted = new HashSet<string>(); // guards against looping on a command a run left ready (e.g. re-queued via re-plan)
        var outcomes = new List<CommandOutcome>();

        while (true)
        {
            var next = scan().FirstOrDefault(c =>
                c.Cmd is { Status: CmdStatus.Ready } && !attempted.Contains(c.Id));
            if (next is null)
                break;

            attempted.Add(next.Id);
            var code = await runCommand(next, session);
            outcomes.Add(new CommandOutcome(next.Id, code));

            // An aborted command never stops the batch (FR-015/FR-025); systemic problems do —
            // a dirty tree or missing environment would just refuse every remaining command.
            if (code is ExitCodes.PreconditionRefused or ExitCodes.EnvironmentError)
            {
                info($"stopping batch: {next.Id} hit a systemic problem (exit {code})");
                return (code, outcomes);
            }
        }

        if (outcomes.Count == 0)
        {
            info("nothing ready");
            return (ExitCodes.Success, outcomes);
        }

        var exit = outcomes.Any(o => o.ExitCode == ExitCodes.TaskFailed) ? ExitCodes.TaskFailed
            : outcomes.Any(o => o.ExitCode != ExitCodes.Success) ? ExitCodes.UsageError
            : ExitCodes.Success;
        return (exit, outcomes);
    }
}
