using System.Runtime.InteropServices;
using MagnaFlow.MfRun.Config;
using MagnaFlow.MfRun.Infrastructure;
using MagnaFlow.MfRun.Runtime;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

if (args[0] is "--help" or "-h")
{
    PrintUsage();
    return 0;
}

string verb = args[0];
if (verb is not ("start" or "stop" or "restart" or "status" or "promote"))
{
    Console.Error.WriteLine($"mf-run: unknown command '{verb}'");
    PrintUsage();
    return 2;
}

string? serviceArg = null;
string? projectArg = null;
var jsonOutput = false;

for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project" when i + 1 < args.Length:
            projectArg = args[++i];
            break;
        case "--json":
            jsonOutput = true;
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        case var a when a.StartsWith('-'):
            Console.Error.WriteLine($"mf-run: unknown argument '{a}'");
            PrintUsage();
            return 2;
        default:
            if (serviceArg is not null || verb == "promote")
            {
                Console.Error.WriteLine($"mf-run: unexpected argument '{args[i]}'");
                PrintUsage();
                return 2;
            }
            serviceArg = args[i];
            break;
    }
}

var projectRoot = Path.GetFullPath(projectArg ?? Directory.GetCurrentDirectory());

var (config, configError) = RunConfig.Load(projectRoot);
if (config is null)
{
    Console.Error.WriteLine($"mf-run: {configError}");
    return 2;
}

void Log(string message) => Console.WriteLine(message);

var spawner = new SystemProcessSpawner();
var clock = new SystemClock();
var manager = new ServiceManager(spawner, clock, new TcpPortProbe(), projectRoot, Log);

if (verb == "promote")
{
    if (config.Stable is null)
    {
        Console.WriteLine("mf-run: no stable instance configured");
        return 0;
    }
    var stable = new StableInstance(spawner, clock, manager, projectRoot, config.Stable, Log,
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
    return await stable.PromoteAsync();
}

if (config.Services.Count == 0 && (config.Stable is null || (serviceArg is null && verb != "status")))
{
    if (verb == "status" && jsonOutput)
        Console.WriteLine(StatusJson.Serialize([]));
    else
        Console.WriteLine("mf-run: no services configured");
    return 0;
}

var (targets, targetError) = RunTargets.Select(config, serviceArg, projectRoot);
if (targets is null)
{
    Console.Error.WriteLine($"mf-run: {targetError}");
    return 2;
}

switch (verb)
{
    case "start":
        return (await manager.StartAsync(targets)).All(o => o.Success) ? 0 : 1;

    case "stop":
        return manager.Stop(targets).All(o => o.Success) ? 0 : 1;

    case "restart":
        return (await manager.RestartAsync(targets)).All(o => o.Success) ? 0 : 1;

    case "status":
        // "All" never includes stable, so its entry is appended for display only and the exit code
        // stays the services' own; `status stable` is judged by the stable process alone.
        var isStableOnly = serviceArg == StableConfig.ServiceName && config.Stable is not null;
        var statuses = isStableOnly ? [] : (await manager.StatusAsync(targets)).ToList();
        var exitCode = statuses.All(s => s.Running) ? 0 : 1;
        if (config.Stable is not null && (serviceArg is null || isStableOnly))
        {
            var stableEntry = await StableInstance.StatusAsync(manager, projectRoot, config.Stable);
            statuses.Add(stableEntry);
            if (isStableOnly)
                exitCode = stableEntry.Running ? 0 : 1;
        }

        if (jsonOutput)
        {
            Console.WriteLine(StatusJson.Serialize(statuses));
        }
        else
        {
            foreach (var status in statuses)
            {
                var url = status.Url is null ? "" : $" {status.Url}";
                var portSuffix = status.PortListening switch
                {
                    true => " (port listening)",
                    false => " (port not listening)",
                    null => "",
                };
                Console.WriteLine((status.Running
                    ? $"{status.Name}: running (pid {status.Pid}){url}{portSuffix}"
                    : $"{status.Name}: stopped [{status.Reason}]{url}{portSuffix}") + StableSuffix(status));
            }
        }
        return exitCode;

    default:
        return 2; // unreachable, verb already validated above
}

static string StableSuffix(ServiceStatusEntry status)
{
    if (status.Stable != true)
        return "";
    if (status.State is null)
        return " — never promoted";
    var sha = status.Sha is null ? "" : $" {status.Sha[..Math.Min(7, status.Sha.Length)]}{(status.Dirty == true ? " (dirty)" : "")}";
    var message = status.Message is null ? "" : $": {status.Message}";
    return $" — {status.State}{sha} at {status.At}{message}";
}

static void PrintUsage()
{
    Console.WriteLine("""
        mf-run: starts, stops, and reports on a project's own configured run.services.

        Usage: mf-run <start|stop|restart|status> [service] [--project <path>] [--json]
               mf-run promote [--project <path>]

          service            Only act on this service (default: all, in config list order).
                             `stable` acts on the stable instance (run.stable); "all" never includes it.
          --project <path>   Target project root (default: current directory).
          --json             status only: print [{name, running, pid?, url?, reason?, portListening?}]
                              instead of the human-readable lines. Exit-code semantics are unchanged.

        Reads run.services from <project>/.magnaflow/config.yml. A project with no `run:`
        block has nothing to do: every command prints a message and exits 0.

        promote publishes run.stable into .magnaflow/stable/next/, swaps it in as current/ and
        starts it (the old build stays in prev/). Exit 3: another promote is running.
        """);
}
