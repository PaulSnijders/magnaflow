using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.WorkerController.Config;

/// <summary>
/// .magnaflow/config.yml — the contract between a target project and any controller
/// (contracts/file-formats.md). Build/test commands are required; everything else defaults.
/// </summary>
public sealed class ProjectConfig
{
    public required IReadOnlyList<string> BuildCommands { get; init; }
    public required IReadOnlyList<string> TestCommands { get; init; }
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public string AgentCommand { get; init; } = "claude";
    public IReadOnlyList<string> AgentArgs { get; init; } = [];

    /// <summary>Substitutable the same way as AgentCommand (ontwerp-v0.1.md "Worker integration").
    /// Only ever invoked when HasRunServices is true — a project with no `run:` block never spawns
    /// mf-run at all, so this default is inert for every project that doesn't opt in.</summary>
    public string RunCommand { get; init; } = "mf-run";

    /// <summary>Whether the target project's own `run:` block configures at least one service
    /// (tools/mf-run's own `run.services`) — the worker only knows "yes/no", never the service
    /// list itself: mf-run stays the sole owner of what "start"/"stop" mean (no library coupling,
    /// process spawn and exit code only).</summary>
    public bool HasRunServices { get; init; }

    public static string ConfigPath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "config.yml");

    /// <summary>Loads and validates config; on failure returns a message listing exactly what is missing.</summary>
    public static (ProjectConfig? Config, string? Error) Load(string projectRoot)
    {
        var path = ConfigPath(projectRoot);
        if (!File.Exists(path))
            return (null, $"no project configuration found at {path}");

        ConfigDto dto;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            dto = deserializer.Deserialize<ConfigDto>(File.ReadAllText(path)) ?? new ConfigDto();
        }
        catch (Exception ex)
        {
            return (null, $"invalid YAML in {path}: {ex.Message}");
        }

        var problems = new List<string>();
        var buildCommands = ResolveCommands(dto.Build, "build", problems);
        var testCommands = ResolveCommands(dto.Test, "test", problems);
        if (problems.Count > 0)
            return (null, $"{path} is missing required field(s): {string.Join(", ", problems)}");

        return (new ProjectConfig
        {
            BuildCommands = buildCommands!,
            TestCommands = testCommands!,
            MaxAttempts = dto.Defaults?.MaxAttempts ?? 3,
            CommandTimeout = TimeSpan.FromMinutes(dto.Defaults?.CommandTimeoutMinutes ?? 30),
            AgentCommand = string.IsNullOrWhiteSpace(dto.Agent?.Command) ? "claude" : dto.Agent.Command.Trim(),
            AgentArgs = dto.Agent?.Args ?? [],
            RunCommand = string.IsNullOrWhiteSpace(dto.Run?.Command) ? "mf-run" : dto.Run.Command.Trim(),
            HasRunServices = dto.Run?.Services is { Count: > 0 },
        }, null);
    }

    /// <summary>Normalizes a section's command:/commands: to a list (contracts/file-formats.md); exactly one form is required.</summary>
    private static List<string>? ResolveCommands(CommandDto? dto, string section, List<string> problems)
    {
        var hasCommand = !string.IsNullOrWhiteSpace(dto?.Command);
        var hasCommands = dto?.Commands is { Count: > 0 };

        if (hasCommand && hasCommands)
        {
            problems.Add($"{section}.command and {section}.commands are both set (use exactly one)");
            return null;
        }
        if (hasCommand)
            return [dto!.Command!.Trim()];
        if (hasCommands)
            return dto!.Commands!.Select(c => c.Trim()).ToList();

        problems.Add(dto?.Commands is { Count: 0 }
            ? $"{section}.commands is empty (needs at least one command)"
            : $"{section}.command or {section}.commands");
        return null;
    }

    private sealed class ConfigDto
    {
        public CommandDto? Build { get; set; }
        public CommandDto? Test { get; set; }
        public DefaultsDto? Defaults { get; set; }
        public AgentDto? Agent { get; set; }
        public RunDto? Run { get; set; }
    }

    private sealed class CommandDto
    {
        public string? Command { get; set; }
        public List<string>? Commands { get; set; }
    }

    private sealed class DefaultsDto
    {
        public int? MaxAttempts { get; set; }
        public int? CommandTimeoutMinutes { get; set; }
    }

    private sealed class AgentDto
    {
        public string? Command { get; set; }
        public List<string>? Args { get; set; }
    }

    /// <summary>Only `command` and the presence/count of `services` matter here — the service
    /// list's own shape (name/command/args/workdir/url) is entirely tools/mf-run's concern.</summary>
    private sealed class RunDto
    {
        public string? Command { get; set; }
        public List<object>? Services { get; set; }
    }
}
