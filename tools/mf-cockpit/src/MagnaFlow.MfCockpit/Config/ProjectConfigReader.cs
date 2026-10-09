using System.Security.Cryptography;
using System.Text;
using MagnaFlow.MfCockpit.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagnaFlow.MfCockpit.Config;

/// <summary>
/// Read-only parsing of the target project's own .magnaflow/config.yml (ontwerp-v0.3.md "The
/// Config page") for the summary card, the run-status "configured?" check, and the PUT save flow's
/// validation. Own implementation, not a reference to MagnaFlow.WorkerController.Config.
/// ProjectConfig — same decoupling every tool in this repo keeps (no library coupling, ontwerp-
/// v0.1.md "Tech"). Tolerant everywhere except Validate(): a file that's already broken on disk
/// still needs to display *something* in the summary card and the run-status check.
/// </summary>
public static class ProjectConfigReader
{
    private static readonly string[] KnownTopLevelKeys = ["build", "test", "defaults", "agent", "run"];

    public static string ConfigPath(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "config.yml");

    public static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public static ProjectConfigSummaryDto Summarize(string content)
    {
        var dto = TryDeserialize(content) ?? new ConfigDto();
        return new ProjectConfigSummaryDto(
            ResolveCommands(dto.Build),
            ResolveCommands(dto.Test),
            dto.Defaults?.MaxAttempts ?? 3,
            ServiceNames(dto) ?? []);
    }

    /// <summary>Null when the project has no `run:` block at all — including when the file fails to
    /// parse — ontwerp-v0.3.md's "configured: false" shape: the run card/chip simply doesn't appear
    /// rather than surfacing a YAML error in a place meant for process status. A `run:` block
    /// present with an empty (or absent) `services:` list is still "configured" (non-null, possibly
    /// empty) — the card ends up hidden anyway on an empty list, same visual outcome.</summary>
    public static IReadOnlyList<string>? ReadRunServiceNames(string content)
    {
        var dto = TryDeserialize(content);
        return dto is null ? null : ServiceNames(dto);
    }

    /// <summary>Whether the project opts into mf-run's stable instance (`run.stable`) — presence
    /// only, its shape is mf-run's concern. False for a file that fails to parse.</summary>
    public static bool HasRunStable(string content) => TryDeserialize(content)?.Run?.Stable is not null;

    /// <summary>PUT's own gate (ontwerp-v0.3.md "Save flow"): a real YAML syntax error is a
    /// rejection (400, with position info for display); an unrecognized top-level key is only ever
    /// a warning — config.yml is shared by tools with different vocabularies, the cockpit must not
    /// become the schema police for all of them.</summary>
    public static (IReadOnlyList<string> Warnings, string? ParseError) Validate(string content)
    {
        Dictionary<string, object?> raw;
        try
        {
            raw = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(content)
                  ?? new Dictionary<string, object?>();
        }
        catch (YamlException ex)
        {
            return ([], $"line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}");
        }

        var unknown = raw.Keys.Where(k => !KnownTopLevelKeys.Contains(k, StringComparer.Ordinal)).ToList();
        IReadOnlyList<string> warnings = unknown.Count > 0
            ? [$"unrecognized top-level key(s): {string.Join(", ", unknown)}"]
            : [];
        return (warnings, null);
    }

    private static ConfigDto? TryDeserialize(string content)
    {
        try { return Deserializer().Deserialize<ConfigDto>(content); }
        catch (YamlException) { return null; }
    }

    private static IReadOnlyList<string>? ServiceNames(ConfigDto dto)
    {
        if (dto.Run is null)
            return null;
        var services = dto.Run.Services ?? [];
        return services.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).ToList();
    }

    private static IReadOnlyList<string> ResolveCommands(CommandDto? dto)
    {
        if (!string.IsNullOrWhiteSpace(dto?.Command))
            return [dto.Command.Trim()];
        if (dto?.Commands is { Count: > 0 })
            return dto.Commands.Select(c => c.Trim()).ToList();
        return [];
    }

    private static IDeserializer Deserializer() =>
        new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    private sealed class ConfigDto
    {
        public CommandDto? Build { get; set; }
        public CommandDto? Test { get; set; }
        public DefaultsDto? Defaults { get; set; }
        public RunSectionDto? Run { get; set; }
    }

    private sealed class CommandDto
    {
        public string? Command { get; set; }
        public List<string>? Commands { get; set; }
    }

    private sealed class DefaultsDto
    {
        public int? MaxAttempts { get; set; }
    }

    private sealed class RunSectionDto
    {
        public List<ServiceNameDto>? Services { get; set; }
        public object? Stable { get; set; }
    }

    private sealed class ServiceNameDto
    {
        public string? Name { get; set; }
    }
}
