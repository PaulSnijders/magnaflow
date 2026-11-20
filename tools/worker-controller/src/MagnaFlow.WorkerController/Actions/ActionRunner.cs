using System.Runtime.InteropServices;
using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Infrastructure;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Actions;

/// <summary>
/// `mf-worker action &lt;name&gt;`: runs one project action once, in the order of
/// docs/specs/worker/action.md — read the file, check what the action needs, the guards, the
/// condition (schedule only), run, check the tree, record. mf-watch relies on the exit codes, and on
/// a refusal or a skip being exactly one stdout line `refused: &lt;reason&gt;` / `skipped: &lt;reason&gt;`.
/// </summary>
public sealed class ActionRunner(
    IGitClient git,
    IProcessRunner processes,
    Func<ProjectConfig, IAgentRunner> agentFactory,
    string projectRoot,
    Action<string> output,
    Action<string> error,
    Func<DateTimeOffset>? clock = null,
    bool? isWindows = null)
{
    public const string RefusedPrefix = "refused: ";
    public const string SkippedPrefix = "skipped: ";
    public static readonly TimeSpan ConditionTimeout = TimeSpan.FromSeconds(30);
    private const string ConditionSkip = "condition";

    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.Now);
    private readonly bool _isWindows = isWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public async Task<int> RunAsync(string name, string trigger)
    {
        // 1. The definition.
        var (actions, loadError) = ActionsFile.Load(projectRoot);
        if (actions is null)
        {
            error(loadError!);
            return ExitCodes.UsageError;
        }
        var action = actions.FirstOrDefault(a => a.Name == name);
        if (action is null)
        {
            error($"unknown action '{name}' (not in {ActionsFile.PathFor(projectRoot)})");
            return ExitCodes.UsageError;
        }

        // 2. What this kind of action needs before it can run at all.
        string? scriptPath = null;
        IAgentRunner? agent = null;
        if (action.Script is not null)
        {
            var resolved = ScriptResolver.Resolve(Path.GetFullPath(Path.Combine(projectRoot, action.Script)), _isWindows);
            if (resolved.Path is null)
            {
                error($"action '{name}': script not found: tried {string.Join(", ", resolved.Attempted)}");
                return ExitCodes.UsageError;
            }
            scriptPath = resolved.Path;
        }
        else
        {
            var (config, configError) = ProjectConfig.Load(projectRoot);
            if (config is null)
            {
                error(configError!);
                return ExitCodes.UsageError;
            }
            agent = agentFactory(config.WithAgentTimeout(action.Timeout));
            if (!await agent.IsAvailableAsync(projectRoot))
            {
                error($"agent '{config.AgentCommand}' is not available; install/authenticate it or set agent.command in .magnaflow/config.yml");
                return ExitCodes.EnvironmentError;
            }
        }

        // 3. Guards: never touch a tree that someone else is working in.
        if (!await git.IsAvailableAsync())
        {
            error("git is not available on this machine");
            return ExitCodes.EnvironmentError;
        }
        var running = PromptScanner.Scan(projectRoot).FirstOrDefault(c => c.Cmd?.Status == CmdStatus.Running);
        var refusal = running is not null
            ? $"a command is running ({running.Id})"
            : !await git.IsWorkingTreeCleanAsync() ? "the working tree is dirty" : null;
        if (refusal is not null)
        {
            var last = ActionLastRun.Load(projectRoot, name);
            (last with { RefusedAt = _now(), Refused = refusal }).Save(projectRoot, name);
            output(RefusedPrefix + refusal);
            return ExitCodes.PreconditionRefused;
        }

        // 4. The condition (schedule only), then 5. run, appending to the action log as output
        // arrives. One log block holds both: the header, the condition's output, then the run's.
        var start = _now();
        string result;
        using (var log = ActionLog.OpenAppend(projectRoot, name))
        {
            var gate = new object();
            void Append(string line)
            {
                lock (gate) log.WriteLine(line);
            }

            log.WriteLine(ActionLog.Header(name, trigger, start));

            if (trigger == "schedule" && action.Condition is not null)
            {
                var skip = await EvaluateConditionAsync(action.Condition, action.ConditionArgs, Append);
                if (skip is not null)
                {
                    var skipSeconds = (int)Math.Round((_now() - start).TotalSeconds);
                    Append(ActionLog.ResultLine(skip == ConditionSkip ? "skipped (condition)" : skip, skipSeconds));
                    var last = ActionLastRun.Load(projectRoot, name);
                    (last with { SkippedAt = start, Skipped = skip }).Save(projectRoot, name);
                    output(SkippedPrefix + skip);
                    return skip == ConditionSkip ? ExitCodes.Success : ExitCodes.TaskFailed;
                }
            }

            output($"action {name} ({trigger}): started");
            if (scriptPath is not null)
                result = await RunScriptAsync(action, scriptPath, line => { Append(line); output(line); });
            else
                result = await RunPromptAsync(action, agent!, Append);
        } // flushed and closed before the tree is looked at

        // 6. A tree left dirty would make the worker refuse every next command.
        if (result == "ok" && !await git.IsWorkingTreeCleanAsync())
            result = "failed (left the tree dirty)";

        // 7. Record.
        var seconds = (int)Math.Round((_now() - start).TotalSeconds);
        using (var log = ActionLog.OpenAppend(projectRoot, name))
            log.WriteLine(ActionLog.ResultLine(result, seconds));
        new ActionLastRun(start, trigger, result, seconds, null, null, null, null).Save(projectRoot, name);
        output($"action {name}: {result} — {seconds}s");

        return result == "ok" ? ExitCodes.Success : ExitCodes.TaskFailed;
    }

    /// <summary>Runs the condition, its output to the log only. Null: it holds, run the action.
    /// Otherwise the skip reason: `condition` (exit 1) or `condition error (…)`.</summary>
    private async Task<string?> EvaluateConditionAsync(string condition, IReadOnlyList<string> args, Action<string> append)
    {
        var resolved = ScriptResolver.Resolve(Path.GetFullPath(Path.Combine(projectRoot, condition)), _isWindows);
        if (resolved.Path is null)
        {
            append($"condition not found: tried {string.Join(", ", resolved.Attempted)}");
            return "condition error (not found)";
        }

        ProcessResult run;
        try
        {
            run = await processes.RunExecutableAsync(resolved.Path, args, projectRoot, append, ConditionTimeout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            append($"could not start {resolved.Path}: {ex.Message}");
            return "condition error (exit -1)";
        }
        return run.TimedOut ? "condition error (timeout)"
            : run.ExitCode == 0 ? null
            : run.ExitCode == 1 ? ConditionSkip
            : $"condition error (exit {run.ExitCode})";
    }

    private async Task<string> RunScriptAsync(ActionDefinition action, string scriptPath, Action<string> onLine)
    {
        ProcessResult run;
        try
        {
            run = await processes.RunExecutableAsync(scriptPath, action.Args, projectRoot, onLine, action.Timeout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onLine($"could not start {scriptPath}: {ex.Message}");
            return "failed (exit -1)";
        }
        return Outcome(run.ExitCode, run.TimedOut);
    }

    private async Task<string> RunPromptAsync(ActionDefinition action, IAgentRunner agent, Action<string> append)
    {
        // A fresh session every time: an action is not a command, it has nothing to resume.
        var run = await agent.RunAsync(action.Prompt!, resumeSessionId: null, projectRoot, append);
        if (!string.IsNullOrEmpty(run.FinalText))
        {
            append(run.FinalText);
            output(run.FinalText);
        }
        return Outcome(run.ExitCode, run.TimedOut);
    }

    private static string Outcome(int exitCode, bool timedOut) =>
        timedOut ? "timeout" : exitCode == 0 ? "ok" : $"failed (exit {exitCode})";
}
