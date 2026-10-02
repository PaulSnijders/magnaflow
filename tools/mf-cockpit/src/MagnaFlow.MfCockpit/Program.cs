using MagnaFlow.MfCockpit.Api;
using MagnaFlow.MfCockpit.Chat;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Live;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Projects;
using MagnaFlow.MfCockpit.Prompts;
using MagnaFlow.MfCockpit.Watch;

// No git the cockpit spawns should ever block on a credentials prompt: it runs headless, often over
// a remote session (ontwerp-v0.5.md item 7, write #8 "git pull"). Set once, inherited by every git
// child process (CliWrap inherits the parent environment) — turns a would-be prompt into a fast
// failure the Git card can show, instead of a wedged request.
Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0");

var cockpitLog = new FileCockpitLog();

var (configPath, isLegacyConfigFileName) = CockpitConfig.LocatePath(args);
var (config, configError, configNotice) = CockpitConfig.Load(configPath, isLegacyConfigFileName);
if (config is null)
{
    cockpitLog.Append($"startup failed: {configError}");
    Console.Error.WriteLine($"mf-cockpit: {configError}");
    Environment.Exit(2);
    return;
}
if (configNotice is not null)
{
    cockpitLog.Append($"config notice: {configNotice}");
    Console.Error.WriteLine(configNotice);
}

cockpitLog.Append(
    $"startup: config={config.ConfigPath} run.command={config.Run.Command} chat.command={config.Chat.Command} bind={config.Bind} port={config.Port}");

// ContentRootPath defaults to Directory.GetCurrentDirectory() — wherever the operator's shell
// happened to be, not the binary's own folder. wwwroot (and a same-directory magnaflow.yml) must
// resolve next to the exe regardless of invocation cwd, same posture as WatchConfig.LocatePath().
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.WebHost.UseUrls($"http://{config.Bind}:{config.Port}");

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(config.Chat);
builder.Services.AddSingleton(config.Run);
builder.Services.AddSingleton(config.Watch);
builder.Services.AddSingleton(config.NewProject);
builder.Services.AddSingleton(new ProjectRegistry(config.Projects));
builder.Services.AddSingleton<ICockpitLog>(cockpitLog);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IProcessRunner, CliWrapProcessRunner>();
builder.Services.AddSingleton<IGitClient, GitClient>();
builder.Services.AddSingleton<IChatRunner, ClaudeChatRunner>();
builder.Services.AddSingleton<IRunClient, MfRunClient>();
builder.Services.AddSingleton<IWatchProcessSpawner, SystemWatchProcessSpawner>();
builder.Services.AddSingleton<IWatchControl>(sp => OperatingSystem.IsWindows()
    ? new WindowsWatchControl(sp.GetRequiredService<WatchClientConfig>(), sp.GetRequiredService<IWatchProcessSpawner>())
    : new LinuxWatchControl(sp.GetRequiredService<IProcessRunner>()));
// The only two endpoints that spawn processes, memoized per project for a few seconds
// (docs/prompts/0010). Their own write actions invalidate their project's entry.
builder.Services.AddSingleton(sp => new TtlCache<ProjectGitInfoDto>(sp.GetRequiredService<IClock>(), TimeSpan.FromSeconds(3)));
builder.Services.AddSingleton(sp => new TtlCache<RunStatusDto>(sp.GetRequiredService<IClock>(), TimeSpan.FromSeconds(3)));
builder.Services.AddSingleton<ChatSessionStore>();
builder.Services.AddSingleton<ScratchpadStore>();
builder.Services.AddSingleton<DraftWriter>();
builder.Services.AddSingleton<ProjectScaffolder>();
builder.Services.AddSingleton<SseHub>();
// One instance, exposed both as the hosted service and as IProjectWatcherRegistry (write #7 needs
// to dispose a single project's watcher on remove — ontwerp-v0.5.md item 4).
builder.Services.AddSingleton<ProjectWatchersHostedService>();
builder.Services.AddSingleton<IProjectWatcherRegistry>(sp => sp.GetRequiredService<ProjectWatchersHostedService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProjectWatchersHostedService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapProjectsApi();
app.MapOverviewApi();
app.MapCommandsApi();
app.MapWatchApi();
app.MapSpecsApi();
app.MapEventsApi();
app.MapChatApi();
app.MapConfigApi();
app.MapGitApi();
app.MapRunApi();
app.MapProjectConfigApi();
app.MapNewProjectApi();
app.MapScratchpadApi();

Console.WriteLine($"mf-cockpit: listening on http://{config.Bind}:{config.Port} ({config.Projects.Count} project(s) configured)");
app.Run();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; (integration tests) can host this app in-process.</summary>
public partial class Program;
