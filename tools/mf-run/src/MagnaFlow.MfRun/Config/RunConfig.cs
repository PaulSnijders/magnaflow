using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfRun.Config;

/// <summary>
/// The project's own .magnaflow/config.yml — same file worker-controller reads, same parsing
/// approach as MagnaFlow.WorkerController.Config.ProjectConfig (ontwerp-v0.1.md "Config"). mf-run
/// only looks at the top-level `run:` key; every other section (build/test/agent/...) is ignored
/// via IgnoreUnmatchedProperties, same technique mf-watch's own separate config file uses. A
/// missing file, or a file with no `run:` block, is not an error — a console app simply has no
/// services (ontwerp-v0.1.md "Config").
/// </summary>
public sealed class RunConfig
{
    public IReadOnlyList<ServiceConfig> Services { get; init; } = [];

    public static string ConfigPath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "config.yml");

    public static (RunConfig? Config, string? Error) Load(string projectRoot)
    {
        var path = ConfigPath(projectRoot);
        if (!File.Exists(path))
            return (new RunConfig(), null);

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

        var serviceDtos = dto.Run?.Services ?? [];
        var problems = new List<string>();
        var services = new List<ServiceConfig>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var serviceDto in serviceDtos)
        {
            if (string.IsNullOrWhiteSpace(serviceDto.Name))
            {
                problems.Add("run.services has an entry with no name");
                continue;
            }
            if (string.IsNullOrWhiteSpace(serviceDto.Command))
            {
                problems.Add($"run.services[{serviceDto.Name}] has no command");
                continue;
            }
            if (!seenNames.Add(serviceDto.Name.Trim()))
            {
                problems.Add($"run.services has more than one service named '{serviceDto.Name.Trim()}'");
                continue;
            }

            services.Add(new ServiceConfig(
                Name: serviceDto.Name.Trim(),
                Command: serviceDto.Command.Trim(),
                Args: serviceDto.Args ?? [],
                Workdir: string.IsNullOrWhiteSpace(serviceDto.Workdir) ? null : serviceDto.Workdir.Trim(),
                Url: string.IsNullOrWhiteSpace(serviceDto.Url) ? null : serviceDto.Url.Trim()));
        }

        if (problems.Count > 0)
            return (null, $"{path}: {string.Join("; ", problems)}");

        return (new RunConfig { Services = services }, null);
    }

    private sealed class ConfigDto
    {
        public RunDto? Run { get; set; }
    }

    private sealed class RunDto
    {
        public List<ServiceDto>? Services { get; set; }
    }

    private sealed class ServiceDto
    {
        public string? Name { get; set; }
        public string? Command { get; set; }
        public List<string>? Args { get; set; }
        public string? Workdir { get; set; }
        public string? Url { get; set; }
    }
}

public sealed record ServiceConfig(string Name, string Command, IReadOnlyList<string> Args, string? Workdir, string? Url);
