using MagnaFlow.MfWatch.Config;
using MagnaFlow.MfWatch.Infrastructure;
using MagnaFlow.MfWatch.Notify;
using MagnaFlow.MfWatch.Polling;
using MagnaFlow.MfWatch.Runtime;
using MagnaFlow.MfWatch.Watch;

string? projectArg = null;
string? configArg = null;
var once = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project" when i + 1 < args.Length:
            projectArg = args[++i];
            break;
        case "--config" when i + 1 < args.Length:
            configArg = args[++i];
            break;
        case "--once":
            once = true;
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"mf-watch: unknown argument '{args[i]}'");
            PrintUsage();
            return 2;
    }
}

var projectRoot = Path.GetFullPath(projectArg ?? Directory.GetCurrentDirectory());
var (configPath, isLegacyConfigFileName) = WatchConfig.LocatePath(configArg);

var (config, configError, configNotice) = WatchConfig.Load(configPath, isLegacyConfigFileName);
if (config is null)
{
    Console.Error.WriteLine($"mf-watch: {configError}");
    return 2;
}
if (configNotice is not null)
    Console.Error.WriteLine(configNotice);

using var instanceLock = InstanceLock.TryAcquire(projectRoot);
if (instanceLock is null)
{
    Console.Error.WriteLine($"mf-watch: another instance is already running against '{projectRoot}' ({InstanceLock.LockPath(projectRoot)} is locked)");
    return 3;
}

using var logger = new Logger(Path.Combine(projectRoot, ".magnaflow", "mf-watch.log"));
void Log(string message) => logger.Log(message);

Log($"mf-watch starting: project={projectRoot} config={configPath} once={once}");
if (configNotice is not null)
    Log(configNotice);

var processes = new CliWrapProcessRunner();
var git = new GitClient(processes, projectRoot);
var notifier = new ShellNotifier(processes, config.NotifyCommand, projectRoot, Log);
var loop = new WatchLoop(config, git, processes, projectRoot, Log, notifier);
var scheduler = new BackoffScheduler(config.IntervalMin, config.IntervalMax, config.IdleGrace, new SystemClock());

using var cts = new CancellationTokenSource();
ConsoleCancelEventHandler? onCancel = null;
onCancel = (_, e) =>
{
    e.Cancel = true;
    Log("shutdown requested, finishing current poll...");
    cts.Cancel();
    Console.CancelKeyPress -= onCancel;
};
Console.CancelKeyPress += onCancel;

if (config.GitSync && !await git.IsAvailableAsync())
{
    Log("git is not available on this machine, but git_sync is enabled");
    Console.Error.WriteLine("mf-watch: git is not available on this machine, but git_sync is enabled");
    return 4;
}

try
{
    while (true)
    {
        var activity = await loop.PollOnceAsync(cts.Token);
        scheduler.RecordPoll(activity);

        if (once)
            break;
        if (cts.IsCancellationRequested)
            break;

        Log($"sleeping {scheduler.Current}");
        try
        {
            // Sliced, not one long delay, so a wake file can cut the sleep short (Polling/WakeFile.cs).
            if (await WakeFile.SleepAsync(scheduler.Current, projectRoot, cts.Token))
                Log("wake requested (.magnaflow/mf-watch.wake) — polling now");
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}
catch (OperationCanceledException)
{
    // cancelled mid-poll; fall through to a clean shutdown
}

Log("mf-watch stopped");
return 0;

static void PrintUsage()
{
    Console.WriteLine("""
        mf-watch: polls docs/prompts/ for ready commands and dispatches mf-worker.

        Usage: mf-watch [--project <path>] [--config <path>] [--once]

          --project <path>  Target project root (default: current directory).
          --config <path>   magnaflow.yml path (default: looked up — see docs/specs/concepts/machine-config.md).
          --once             Run a single poll cycle, then exit.
        """);
}
