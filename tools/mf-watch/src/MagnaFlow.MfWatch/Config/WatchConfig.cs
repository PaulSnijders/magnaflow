using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfWatch.Config;

/// <summary>
/// magnaflow.yml's `watch:` section — daemon configuration, deliberately separate from a target
/// project's own .magnaflow/config.yml (ontwerp-v0.1.md): mf-watch is a dispatcher that can point
/// at any project, so its config lives next to the binary (or wherever --config says), not inside
/// the project it watches. Same parsing approach as MagnaFlow.WorkerController.Config.ProjectConfig.
/// Fase 7 merged the old standalone mf-watch.yml into magnaflow.yml's `watch:` section, alongside
/// mf-cockpit's `cockpit:` section — see docs/specs/concepts/machine-config.md.
/// </summary>
public sealed class WatchConfig
{
    public bool GitSync { get; init; }
    public string WorkerCommand { get; init; } = "mf-worker";
    public IReadOnlyList<string> WorkerArgs { get; init; } = [];
    public string? NotifyCommand { get; init; }
    public TimeSpan IntervalMin { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan IntervalMax { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan IdleGrace { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan WorkerTimeout { get; init; } = TimeSpan.FromHours(2);

    public const string FileName = "magnaflow.yml";
    public const string LegacyFileName = "mf-watch.yml";

    /// <summary>Resolves the effective config path and whether it was found via the legacy
    /// (pre-fase-7) filename, following the lookup order in docs/specs/concepts/machine-config.md:
    /// (1) --config, explicit and authoritative — a missing --config path is not a further lookup,
    /// it simply yields defaults, same as before fase 7; (2) magnaflow.yml next to the binary;
    /// (3) the user config dir; (4) the legacy mf-watch.yml next to the binary. First hit wins.
    /// baseDirectory/userConfigDirectory are overridable for tests; production callers omit them.</summary>
    public static (string Path, bool IsLegacyFileName) LocatePath(string? explicitConfigArg,
        string? baseDirectory = null, string? userConfigDirectory = null)
    {
        if (explicitConfigArg is not null)
            return (Path.GetFullPath(explicitConfigArg), false);

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

    /// <summary>%APPDATA%\MagnaFlow on Windows, ~/.config/magnaflow elsewhere.</summary>
    public static string DefaultUserConfigDirectory() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagnaFlow")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "magnaflow");

    /// <summary>Loads and validates config; on failure returns a message describing what is wrong.
    /// A missing file is not an error: every field has a documented default (all-optional config).
    /// isLegacyFileName forces old-format (flat, unsectioned) parsing and always emits Notice — set
    /// it when the path came from LocatePath's legacy-filename fallback. Otherwise the shape is
    /// auto-detected: a top-level `watch:` key means sectioned (current) format; recognizable
    /// top-level fields without it mean old-format content under the new filename, which still
    /// parses fine but still gets a Notice.</summary>
    public static (WatchConfig? Config, string? Error, string? Notice) Load(string path, bool isLegacyFileName = false)
    {
        if (!File.Exists(path))
            return (new WatchConfig(), null, null);

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
        if (!isLegacyFileName && dto.Watch is not null)
        {
            section = dto.Watch;
        }
        else
        {
            section = dto;
            if (isLegacyFileName || dto.HasAnyLegacyField())
                notice = $"mf-watch: {path} is in the old mf-watch.yml format — merge it into magnaflow.yml under a top-level 'watch:' section (see docs/specs/concepts/machine-config.md).";
        }

        if (section.IntervalMinMinutes is <= 0)
            return (null, $"{path}: interval_min_minutes must be positive", null);
        if (section.IntervalMaxMinutes is { } max && section.IntervalMinMinutes is { } min && max < min)
            return (null, $"{path}: interval_max_minutes must be >= interval_min_minutes", null);

        return (new WatchConfig
        {
            GitSync = section.GitSync ?? false,
            WorkerCommand = string.IsNullOrWhiteSpace(section.Worker?.Command) ? "mf-worker" : section.Worker.Command.Trim(),
            WorkerArgs = section.Worker?.Args ?? [],
            NotifyCommand = string.IsNullOrWhiteSpace(section.NotifyCommand) ? null : section.NotifyCommand.Trim(),
            IntervalMin = TimeSpan.FromMinutes(section.IntervalMinMinutes ?? 1),
            IntervalMax = TimeSpan.FromMinutes(section.IntervalMaxMinutes ?? 15),
            IdleGrace = TimeSpan.FromMinutes(section.IdleGraceMinutes ?? 30),
            WorkerTimeout = TimeSpan.FromMinutes(section.WorkerTimeoutMinutes ?? 120),
        }, null, notice);
    }

    private class ConfigDto
    {
        public bool? GitSync { get; set; }
        public WorkerDto? Worker { get; set; }
        public string? NotifyCommand { get; set; }
        public double? IntervalMinMinutes { get; set; }
        public double? IntervalMaxMinutes { get; set; }
        public double? IdleGraceMinutes { get; set; }
        public double? WorkerTimeoutMinutes { get; set; }

        public bool HasAnyLegacyField() =>
            GitSync.HasValue || Worker is not null || NotifyCommand is not null ||
            IntervalMinMinutes.HasValue || IntervalMaxMinutes.HasValue ||
            IdleGraceMinutes.HasValue || WorkerTimeoutMinutes.HasValue;
    }

    /// <summary>Top level of magnaflow.yml: the `watch:` section (current format) plus the same
    /// fields flattened at the root (old mf-watch.yml format) — other tools' sections (e.g.
    /// `cockpit:`) are ignored via IgnoreUnmatchedProperties.</summary>
    private sealed class RootDto : ConfigDto
    {
        public ConfigDto? Watch { get; set; }
    }

    private sealed class WorkerDto
    {
        public string? Command { get; set; }
        public List<string>? Args { get; set; }
    }
}
