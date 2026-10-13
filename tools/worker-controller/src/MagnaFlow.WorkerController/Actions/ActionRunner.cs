using System.Runtime.InteropServices;
using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Infrastructure;
using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Actions;

/// <summary>
/// `mf-worker action &lt;name&gt;`: runs one project action once, in the order of
/// docs/specs/worker/action.md — read the file, check what the action needs, the guards, run,
/// check the tree, record. mf-watch relies on the exit codes, and on a refusal being exactly one
/// stdout line `refused: &lt;reason&gt;`.
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
            agent = agentFactory(config.WithCommandTimeout(action.Timeout));
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

        // 4. Run, appending to the action log as output arrives.
        var start = _now();
        output($"action {name} ({trigger}): started");
        string result;
        using (var log = ActionLog.OpenAppend(projectRoot, name))
        {
            var gate = new object();
            void Append(string line)
            {
                lock (gate) log.WriteLine(line);
            }

            log.WriteLine(ActionLog.Header(name, trigger, start));
            if (scriptPath is not null)
                result = await RunScriptAsync(action, scriptPath, line => { Append(line); output(line); });
            else
                result = await RunPromptAsync(action, agent!, Append);
        } // flushed and closed before the tree is looked at

        // 5. A tree left dirty would make the worker refuse every next command.
        if (result == "ok" && !await git.IsWorkingTreeCleanAsync())
            result = "failed (left the tree dirty)";

        // 6. Record.
        var seconds = (int)Math.Round((_now() - start).TotalSeconds);
        using (var log = ActionLog.OpenAppend(projectRoot, name))
            log.WriteLine(ActionLog.ResultLine(result, seconds));
        new ActionLastRun(start, trigger, result, seconds, null, null).Save(projectRoot, name);
        output($"action {name}: {result} — {seconds}s");

        return result == "ok" ? ExitCodes.Success : ExitCodes.TaskFailed;
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
