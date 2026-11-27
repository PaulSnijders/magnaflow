using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.WorkerController.Config;

/// <summary>
/// .magnaflow/config.yml — the contract between a target project and any controller
/// (contracts/file-formats.md). Build commands are required; `test:` is optional (an absent key
/// means no test gate, a present-but-empty one is malformed); everything else defaults.
/// </summary>
public sealed class ProjectConfig
{
    public required IReadOnlyList<CheckCommand> BuildCommands { get; init; }
    public required IReadOnlyList<CheckCommand> TestCommands { get; init; }

    /// <summary>Whether any build/test command carries `paths:` — only then does the worker ask
    /// git which files the run touched (docs/specs/concepts/worker-run.md step 5).</summary>
    public bool HasPathScopedCommands => BuildCommands.Concat(TestCommands).Any(c => c.IsPathScoped);
    public int MaxAttempts { get; init; } = 3;
    /// <summary>Limit per build/test command (defaults.command_timeout_minutes).</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(30);

    private readonly TimeSpan? _agentTimeout;

    /// <summary>Limit per agent invocation (defaults.agent_timeout_minutes); unset, it falls back
    /// to CommandTimeout — also for a config built in code.</summary>
    public TimeSpan AgentTimeout { get => _agentTimeout ?? CommandTimeout; init => _agentTimeout = value; }
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

    /// <summary>Whether the project opts into mf-run's stable instance (`run.stable`). Presence only,
    /// read the same way as HasRunServices: what the block holds is mf-run's concern, the worker
    /// just spawns `promote` after a branchless done (docs/specs/concepts/worker-run.md step 8).</summary>
    public bool HasRunStable { get; init; }

    /// <summary>The same config with another agent timeout: an action runs its prompt under its own
    /// timeout_minutes, not the project's agent timeout. Build/test limits are untouched.</summary>
    public ProjectConfig WithAgentTimeout(TimeSpan timeout) => new()
    {
        BuildCommands = BuildCommands,
        TestCommands = TestCommands,
        MaxAttempts = MaxAttempts,
        CommandTimeout = CommandTimeout,
        AgentTimeout = timeout,
        AgentCommand = AgentCommand,
        AgentArgs = AgentArgs,
        RunCommand = RunCommand,
        HasRunServices = HasRunServices,
        HasRunStable = HasRunStable,
    };

    public static string ConfigPath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "config.yml");

    /// <summary>Loads and validates config; on failure returns a message listing exactly what is missing.</summary>
    public static (ProjectConfig? Config, string? Error) Load(string projectRoot)
    {
        var path = ConfigPath(projectRoot);
        if (!File.Exists(path))
            return (null, $"no project configuration found at {path}");

        ConfigDto dto;
        bool hasTestKey;
        try
        {
            var text = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            dto = deserializer.Deserialize<ConfigDto>(text) ?? new ConfigDto();
            hasTestKey = HasRootKey(text, "test");
        }
        catch (Exception ex)
        {
            return (null, $"invalid YAML in {path}: {ex.Message}");
        }

        var problems = new List<string>();
        var invalid = new List<string>();
        var buildCommands = ResolveCommands(dto.Build, "build", problems, invalid);
        // Only an absent key means "no test gate"; a present-but-unusable test: is malformed,
        // so a typo never switches testing off.
        var testCommands = hasTestKey ? ResolveCommands(dto.Test, "test", problems, invalid) : [];
        if (problems.Count > 0)
            return (null, $"{path} is missing required field(s): {string.Join(", ", problems)}");
        if (invalid.Count > 0)
            return (null, $"{path} has an invalid value: {string.Join("; ", invalid)}");

        var commandTimeoutMinutes = dto.Defaults?.CommandTimeoutMinutes ?? 30;
        var agentTimeoutMinutes = dto.Defaults?.AgentTimeoutMinutes ?? commandTimeoutMinutes;
        if (dto.Defaults?.AgentTimeoutMinutes is < 1)
            return (null, $"{path} has an invalid value: defaults.agent_timeout_minutes must be at least 1");

        return (new ProjectConfig
        {
            BuildCommands = buildCommands!,
            TestCommands = testCommands!,
            MaxAttempts = dto.Defaults?.MaxAttempts ?? 3,
            CommandTimeout = TimeSpan.FromMinutes(commandTimeoutMinutes),
            AgentTimeout = TimeSpan.FromMinutes(agentTimeoutMinutes),
            AgentCommand = string.IsNullOrWhiteSpace(dto.Agent?.Command) ? "claude" : dto.Agent.Command.Trim(),
            AgentArgs = dto.Agent?.Args ?? [],
            RunCommand = string.IsNullOrWhiteSpace(dto.Run?.Command) ? "mf-run" : dto.Run.Command.Trim(),
            HasRunServices = dto.Run?.Services is { Count: > 0 },
            HasRunStable = dto.Run?.Stable is not null,
        }, null);
    }

    /// <summary>Whether the YAML root is a mapping with this key — the DTO cannot tell a bare
    /// `key:` (null) from no key at all.</summary>
    private static bool HasRootKey(string yaml, string key)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents.Count > 0
            && stream.Documents[0].RootNode is YamlMappingNode root
            && root.Children.Keys.Any(k => k is YamlScalarNode { Value: var v } && v == key);
    }

    /// <summary>Normalizes a section's command:/commands: to a list (contracts/file-formats.md); exactly one form is required.
    /// A `commands:` entry is a string or a `{run, paths}` mapping; the single `command:` stays a string.</summary>
    private static List<CheckCommand>? ResolveCommands(CommandDto? dto, string section, List<string> problems, List<string> invalid)
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
        {
            var result = new List<CheckCommand>();
            for (var i = 0; i < dto!.Commands!.Count; i++)
            {
                var entry = ParseEntry(dto.Commands[i], $"{section}.commands[{i + 1}]", invalid);
                if (entry is not null) result.Add(entry);
            }
            return result;
        }

        problems.Add(dto?.Commands is { Count: 0 }
            ? $"{section}.commands is empty (needs at least one command)"
            : $"{section}.command or {section}.commands");
        return null;
    }

    private static CheckCommand? ParseEntry(object? raw, string where, List<string> invalid)
    {
        switch (raw)
        {
            case string s when !string.IsNullOrWhiteSpace(s):
                return s.Trim();
            case IDictionary<object, object> map:
            {
                var unknown = map.Keys.Select(k => k?.ToString() ?? "").Where(k => k is not ("run" or "paths")).ToList();
                if (unknown.Count > 0)
                {
                    invalid.Add($"{where} has unknown key(s) {string.Join(", ", unknown)} (only run and paths)");
                    return null;
                }
                if (!map.TryGetValue("run", out var run) || run is not string runText || string.IsNullOrWhiteSpace(runText))
                {
                    invalid.Add($"{where} needs a run: command");
                    return null;
                }
                if (!map.TryGetValue("paths", out var rawPaths) || rawPaths is not IList<object> list || list.Count == 0)
                {
                    invalid.Add($"{where}.paths must be a non-empty list (leave paths out for a command that always runs)");
                    return null;
                }
                var paths = new List<string>();
                foreach (var item in list)
                {
                    var problem = NormalizePath(item, out var normalized);
                    if (problem is not null)
                    {
                        invalid.Add($"{where}.paths: {problem}");
                        return null;
                    }
                    paths.Add(normalized!);
                }
                return new CheckCommand(runText.Trim(), paths);
            }
            default:
                invalid.Add($"{where} must be a command string or a mapping with run: and paths:");
                return null;
        }
    }

    /// <summary>A repo-relative prefix: '\' becomes '/', a leading "./" is dropped; absolute
    /// paths and ".." segments are refused — there are no globs.</summary>
    private static string? NormalizePath(object? item, out string? normalized)
    {
        normalized = null;
        if (item is not string text || string.IsNullOrWhiteSpace(text))
            return "every entry must be a non-empty path";
        var p = text.Trim().Replace('\\', '/');
        if (p.StartsWith('/') || (p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':'))
            return $"'{text}' is absolute (paths are relative to the repo root)";
        while (p.StartsWith("./")) p = p[2..];
        if (p.Split('/').Contains(".."))
            return $"'{text}' contains '..'";
        if (p.Length == 0 || p == ".")
            return $"'{text}' names the whole repo (leave paths out instead)";
        normalized = p;
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
        /// <summary>Each entry: a string, or a mapping (deserialized as a dictionary).</summary>
        public List<object>? Commands { get; set; }
    }

    private sealed class DefaultsDto
    {
        public int? MaxAttempts { get; set; }
        public int? CommandTimeoutMinutes { get; set; }
        public int? AgentTimeoutMinutes { get; set; }
    }

    private sealed class AgentDto
    {
        public string? Command { get; set; }
        public List<string>? Args { get; set; }
    }

    /// <summary>Only `command`, the presence/count of `services` and the presence of `stable` matter
    /// here — their own shape is entirely tools/mf-run's concern.</summary>
    private sealed class RunDto
    {
        public string? Command { get; set; }
        public List<object>? Services { get; set; }
        public object? Stable { get; set; }
    }
}
