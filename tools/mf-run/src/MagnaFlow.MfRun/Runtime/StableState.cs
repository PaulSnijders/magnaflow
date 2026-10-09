using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using MagnaFlow.MfRun.Config;

namespace MagnaFlow.MfRun.Runtime;

/// <summary>
/// .magnaflow/stable/state.yml — what the last promote did (docs/specs/run/mf-run.md
/// #stable-instance): `state` is building, ready or failed; `sha`/`dirty` describe the tree that was
/// published; `at` is when the state was written; `message` says why it failed. Plain YAML so `cat`
/// suffices, written through YamlDotNet so a message with a colon stays valid.
/// </summary>
public sealed record StableState(string State, string? Sha, bool? Dirty, string? At, string? Message)
{
    public const string Building = "building";
    public const string Ready = "ready";
    public const string Failed = "failed";

    public static string PathFor(string projectRoot) => Path.Combine(StableConfig.StableDirectory(projectRoot), "state.yml");

    /// <summary>Null when the file is missing or unreadable — a stable instance never promoted.</summary>
    public static StableState? Read(string projectRoot)
    {
        var path = PathFor(projectRoot);
        if (!File.Exists(path))
            return null;
        try
        {
            var dto = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<Dto>(File.ReadAllText(path));
            return dto?.State is null ? null : new StableState(dto.State, dto.Sha, dto.Dirty, dto.At, dto.Message);
        }
        catch (Exception ex) when (ex is IOException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    public static void Write(string projectRoot, StableState state)
    {
        var path = PathFor(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var yaml = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build()
            .Serialize(new Dto { State = state.State, Sha = state.Sha, Dirty = state.Dirty, At = state.At, Message = state.Message });
        File.WriteAllText(path, yaml);
    }

    private sealed class Dto
    {
        public string? State { get; set; }
        public string? Sha { get; set; }
        public bool? Dirty { get; set; }
        public string? At { get; set; }
        public string? Message { get; set; }
    }
}
