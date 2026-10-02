using System.Text.Json;
using System.Text.Json.Serialization;

namespace MagnaFlow.MfRun.Runtime;

/// <summary>
/// `mf-run status --json` (ontwerp-v0.1.md "Cockpit (later)", concretized in mf-cockpit's own
/// ontwerp-v0.3.md): an array of {name, running, pid?, url?}, camelCase, null fields omitted so the
/// `?` in the design's own shape notation is literal. Parsing the human-facing lines would be
/// fragile and would turn their wording into an accidental API; this is the honest alternative.
/// </summary>
public static class StatusJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(IReadOnlyList<ServiceStatusEntry> statuses) =>
        JsonSerializer.Serialize(statuses, Options);
}
