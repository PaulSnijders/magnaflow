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
if (verb is not ("start" or "stop" or "restart" or "status"))
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
            if (serviceArg is not null)
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

if (config.Services.Count == 0)
{
    if (verb == "status" && jsonOutput)
        Console.WriteLine(StatusJson.Serialize([]));
    else
        Console.WriteLine("mf-run: no services configured");
    return 0;
}

IReadOnlyList<ServiceConfig> targets;
if (serviceArg is null)
{
    targets = config.Services;
}
else
{
    var match = config.Services.FirstOrDefault(s => s.Name == serviceArg);
    if (match is null)
    {
        Console.Error.WriteLine($"mf-run: unknown service '{serviceArg}'");
        return 2;
    }
    targets = [match];
}

void Log(string message) => Console.WriteLine(message);

var manager = new ServiceManager(new SystemProcessSpawner(), new SystemClock(), new TcpPortProbe(), projectRoot, Log);

switch (verb)
{
    case "start":
        return (await manager.StartAsync(targets)).All(o => o.Success) ? 0 : 1;

    case "stop":
        return manager.Stop(targets).All(o => o.Success) ? 0 : 1;

    case "restart":
        return (await manager.RestartAsync(targets)).All(o => o.Success) ? 0 : 1;

    case "status":
        var statuses = await manager.StatusAsync(targets);
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
                Console.WriteLine(status.Running
                    ? $"{status.Name}: running (pid {status.Pid}){url}{portSuffix}"
                    : $"{status.Name}: stopped [{status.Reason}]{url}{portSuffix}");
            }
        }
        return statuses.All(s => s.Running) ? 0 : 1;

    default:
        return 2; // unreachable, verb already validated above
}

static void PrintUsage()
{
    Console.WriteLine("""
        mf-run: starts, stops, and reports on a project's own configured run.services.

        Usage: mf-run <start|stop|restart|status> [service] [--project <path>] [--json]

          service            Only act on this service (default: all, in config list order).
          --project <path>   Target project root (default: current directory).
          --json             status only: print [{name, running, pid?, url?, reason?, portListening?}]
                              instead of the human-readable lines. Exit-code semantics are unchanged.

        Reads run.services from <project>/.magnaflow/config.yml. A project with no `run:`
        block has nothing to do: every command prints a message and exits 0.
        """);
}
