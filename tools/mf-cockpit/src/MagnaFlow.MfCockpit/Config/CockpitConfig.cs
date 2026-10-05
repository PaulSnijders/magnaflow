using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfCockpit.Config;

/// <summary>One entry in projects: — a name (used in every /api/projects/{name} route and the
/// chat/lane pages) and the working-copy path it points at (ontwerp-v0.1.md "What it shows").</summary>
public sealed class ProjectEntry
{
    public required string Name { get; init; }
    public required string Path { get; init; }
}

public sealed class ChatConfig
{
    public bool Enabled { get; init; } = true;
    public string Command { get; init; } = "claude";
    public IReadOnlyList<string> Args { get; init; } = [];
    /// <summary>Hard timeout on the spawned chat process (ontwerp-v0.1.md "Guards").</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>The Run card's own substitutable seam (ontwerp-v0.3.md "The Run card" — "same
/// substitutable seam as chat.command"): every status read and every button is a spawn of this
/// command, never process logic the cockpit owns itself.</summary>
public sealed class RunClientConfig
{
    public string Command { get; init; } = "mf-run";
    /// <summary>ontwerp-v0.3.md "Guards": "mf-run spawns get a hard timeout (config, default 60 s)".</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>The watch toggle's Windows-only spawn seam (Watch/WindowsWatchControl.cs): the
/// mf-watch executable to spawn detached, PATH-resolved by default like RunClientConfig.Command.
/// Unused on Linux, where the toggle shells out to systemctl/systemd-escape instead.</summary>
public sealed class WatchClientConfig
{
    public string Command { get; init; } = "mf-watch";
}

/// <summary>One entry in cockpit.new_project.templates (ontwerp-v0.4.md "Machine config"): either a
/// recursive directory `copy`, or a `command` spawn with {target}/{name} placeholders — both kinds
/// already exist in practice, this just names them. The browser only ever sends a template name;
/// Command/Args/Source never leave the server (ontwerp-v0.4.md "Guards").</summary>
public sealed class NewProjectTemplate
{
    public required string Name { get; init; }
    public required string Type { get; init; } // "copy" | "command"
    public string? Source { get; init; }
    public string? Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    /// <summary>ontwerp-v0.4.md "Machine config": "timeout_seconds: 300  # default 300".</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(300);
}

/// <summary>cockpit.new_project (ontwerp-v0.4.md "Machine config"): where write #6 ("Add project")
/// scaffolds a brand-new project. Root/SpecKit are null when unconfigured — the "New" tab is then
/// disabled with an explanatory hint rather than half-working (ontwerp-v0.4.md "The dialog").</summary>
public sealed class NewProjectConfig
{
    public string? Root { get; init; }
    public string? SpecKit { get; init; }
    public IReadOnlyList<NewProjectTemplate> Templates { get; init; } = [];
}

/// <summary>
/// magnaflow.yml's `cockpit:` section — every field defaulted (ontwerp-v0.1.md "Tech"), same
/// YAML/parsing approach as MagnaFlow.WorkerController.Config.ProjectConfig and
/// MagnaFlow.MfWatch.Config.WatchConfig. Fase 7 merged the old standalone mf-cockpit.yml into
/// magnaflow.yml's `cockpit:` section, alongside mf-watch's `watch:` section — see
/// docs/specs/concepts/machine-config.md.
/// </summary>
public sealed class CockpitConfig
{
    public int Port { get; init; } = 5210;
    public string Bind { get; init; } = "localhost";
    public IReadOnlyList<ProjectEntry> Projects { get; init; } = [];
    public ChatConfig Chat { get; init; } = new();
    public RunClientConfig Run { get; init; } = new();
    public WatchClientConfig Watch { get; init; } = new();
    public NewProjectConfig NewProject { get; init; } = new();

    /// <summary>The path Load() actually read (or would have read, had it existed) — surfaced via
    /// GET /api/config so "why is it running the default?" is answerable from the dashboard
    /// (docs/prompts/0001-cmd-cockpit-spawn-errors.md).</summary>
    public string ConfigPath { get; init; } = "";

    /// <summary>Whether ConfigPath was located via the deprecated mf-cockpit.yml filename
    /// (LocatePath's legacy-filename fallback) — MagnaflowYmlAppender's write #6 refuses to append
    /// to it (ontwerp-v0.4.md "Writing magnaflow.yml": "the deprecation path doesn't grow new
    /// features").</summary>
    public bool IsLegacyFileName { get; init; }

    public const string FileName = "magnaflow.yml";
    public const string LegacyFileName = "mf-cockpit.yml";

    /// <summary>Resolves the effective config path and whether it was found via the legacy
    /// (pre-fase-7) filename, following the lookup order in docs/specs/concepts/machine-config.md: (1)
    /// --config, or MF_COCKPIT_CONFIG (test injection — WebApplicationFactory reruns this
    /// process's own Program.cs in-proc, where argv isn't test-controlled but environment
    /// variables are) — explicit and authoritative, a missing path here is not a further lookup,
    /// it simply yields defaults, same as before fase 7; (2) magnaflow.yml next to the binary;
    /// (3) the user config dir; (4) the legacy mf-cockpit.yml next to the binary. First hit wins.
    /// baseDirectory/userConfigDirectory are overridable for tests; production callers omit them.</summary>
    public static (string Path, bool IsLegacyFileName) LocatePath(string[] args,
        string? baseDirectory = null, string? userConfigDirectory = null)
    {
        var explicitPath = GetExplicitConfigArg(args) ?? Environment.GetEnvironmentVariable("MF_COCKPIT_CONFIG");
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return (Path.GetFullPath(explicitPath), false);

        baseDirectory ??= AppContext.BaseDirectory;
        userConfigDirectory ??= DefaultUserConfigDirectory();

        var primary = Path.Combine(baseDirectory, FileName);
        if (File.Exists(primary))
            return (primary, false);

        var userPath = Path.Combine(userConfigDirectory, FileName);
        if (File.Exists(userPath))
            return (userPath, false);

        var legacy = Path.Combine(baseDirectory, LegacyFileName);
        if (File.Exists(legacy))
            return (legacy, true);

        return (primary, false); // nothing found anywhere; Load() treats a missing file as defaults
    }

    private static string? GetExplicitConfigArg(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--config")
                return args[i + 1];
        return null;
    }

    /// <summary>%APPDATA%\MagnaFlow on Windows, ~/.config/magnaflow elsewhere.</summary>
    public static string DefaultUserConfigDirectory() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagnaFlow")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "magnaflow");

    /// <summary>Loads and validates config; a missing file is not an error — every field defaults.
    /// isLegacyFileName forces old-format (flat, unsectioned) parsing and always emits Notice — set
    /// it when the path came from LocatePath's legacy-filename fallback. Otherwise the shape is
    /// auto-detected: a top-level `cockpit:` key means sectioned (current) format; recognizable
    /// top-level fields without it mean old-format content under the new filename, which still
    /// parses fine but still gets a Notice.</summary>
    public static (CockpitConfig? Config, string? Error, string? Notice) Load(string path, bool isLegacyFileName = false)
    {
        if (!File.Exists(path))
            return (new CockpitConfig { ConfigPath = path, IsLegacyFileName = isLegacyFileName }, null, null);

        RootDto dto;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            dto = deserializer.Deserialize<RootDto>(File.ReadAllText(path)) ?? new RootDto();
        }
        catch (Exception ex)
        {
            return (null, $"invalid YAML in {path}: {ex.Message}", null);
        }

        ConfigDto section;
        string? notice = null;
        if (!isLegacyFileName && dto.Cockpit is not null)
        {
            section = dto.Cockpit;
        }
        else
        {
            section = dto;
            if (isLegacyFileName || dto.HasAnyLegacyField())
                notice = $"mf-cockpit: {path} is in the old mf-cockpit.yml format — merge it into magnaflow.yml under a top-level 'cockpit:' section (see docs/specs/concepts/machine-config.md).";
        }

        var problems = new List<string>();
        var projects = new List<ProjectEntry>();
        foreach (var p in section.Projects ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Path))
            {
                problems.Add("every projects[] entry needs both name and path");
                continue;
            }
            projects.Add(new ProjectEntry { Name = p.Name.Trim(), Path = Path.GetFullPath(p.Path.Trim()) });
        }
        if (projects.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != projects.Count)
            problems.Add("projects[].name values must be unique");
        if (problems.Count > 0)
            return (null, $"{path}: {string.Join("; ", problems)}", null);

        return (new CockpitConfig
        {
            ConfigPath = path,
            IsLegacyFileName = isLegacyFileName,
            Port = section.Port ?? 5210,
            Bind = string.IsNullOrWhiteSpace(section.Bind) ? "localhost" : section.Bind.Trim(),
            Projects = projects,
            Chat = new ChatConfig
            {
                Enabled = section.Chat?.Enabled ?? true,
                Command = string.IsNullOrWhiteSpace(section.Chat?.Command) ? "claude" : section.Chat.Command.Trim(),
                Args = section.Chat?.Args ?? [],
                Timeout = TimeSpan.FromMinutes(section.Chat?.TimeoutMinutes ?? 5),
            },
            Run = new RunClientConfig
            {
                Command = string.IsNullOrWhiteSpace(section.Run?.Command) ? "mf-run" : section.Run.Command.Trim(),
                Timeout = TimeSpan.FromSeconds(section.Run?.TimeoutSeconds ?? 60),
            },
            Watch = new WatchClientConfig
            {
                Command = string.IsNullOrWhiteSpace(section.Watch?.Command) ? "mf-watch" : section.Watch.Command.Trim(),
            },
            NewProject = new NewProjectConfig
            {
                // Root/SpecKit/template Source are directory paths — normalized with GetFullPath
                // exactly like ProjectEntry.Path above, so a config author's forward-slash YAML and
                // Path.Combine downstream agree on one native separator. Command is deliberately
                // left as-is (Trim only): it may be a bare PATH-resolved name ("claude", "dotnet"),
                // same posture as ChatConfig.Command/RunClientConfig.Command.
                Root = string.IsNullOrWhiteSpace(section.NewProject?.Root) ? null : Path.GetFullPath(section.NewProject.Root.Trim()),
                SpecKit = string.IsNullOrWhiteSpace(section.NewProject?.SpecKit) ? null : Path.GetFullPath(section.NewProject.SpecKit.Trim()),
                Templates = (section.NewProject?.Templates ?? [])
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Type))
                    .Select(t => new NewProjectTemplate
                    {
                        Name = t.Name!.Trim(),
                        Type = t.Type!.Trim(),
                        Source = string.IsNullOrWhiteSpace(t.Source) ? null : Path.GetFullPath(t.Source.Trim()),
                        Command = string.IsNullOrWhiteSpace(t.Command) ? null : t.Command.Trim(),
                        Args = t.Args ?? [],
                        Timeout = TimeSpan.FromSeconds(t.TimeoutSeconds ?? 300),
                    }).ToList(),
            },
        }, null, notice);
    }

    private class ConfigDto
    {
        public int? Port { get; set; }
        public string? Bind { get; set; }
        public List<ProjectDto>? Projects { get; set; }
        public ChatDto? Chat { get; set; }
        public RunDto? Run { get; set; }
        public WatchDto? Watch { get; set; }
        public NewProjectDto? NewProject { get; set; }

        public bool HasAnyLegacyField() =>
            Port.HasValue || Bind is not null || Projects is not null || Chat is not null || Run is not null;
    }

    /// <summary>Top level of magnaflow.yml: the `cockpit:` section (current format) plus the same
    /// fields flattened at the root (old mf-cockpit.yml format) — other tools' sections (e.g.
    /// `watch:`) are ignored via IgnoreUnmatchedProperties.</summary>
    private sealed class RootDto : ConfigDto
    {
        public ConfigDto? Cockpit { get; set; }
    }

    private sealed class ProjectDto
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
    }

    private sealed class ChatDto
    {
        public bool? Enabled { get; set; }
        public string? Command { get; set; }
        public List<string>? Args { get; set; }
        public double? TimeoutMinutes { get; set; }
    }

    private sealed class RunDto
    {
        public string? Command { get; set; }
        public double? TimeoutSeconds { get; set; }
    }

    private sealed class WatchDto
    {
        public string? Command { get; set; }
    }

    private sealed class NewProjectDto
    {
        public string? Root { get; set; }
        public string? SpecKit { get; set; }
        public List<TemplateDto>? Templates { get; set; }
    }

    private sealed class TemplateDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Source { get; set; }
        public string? Command { get; set; }
        public List<string>? Args { get; set; }
        public double? TimeoutSeconds { get; set; }
    }
}
