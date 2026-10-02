using MagnaFlow.WorkerController.Prompts;

namespace MagnaFlow.WorkerController.Commands;

public sealed record StatusRow(string Id, string Title, string Status, string Attempts, bool Warning);

/// <summary>
/// Pure row-building for the status overview (v0.1 FR-016), separated from Spectre rendering
/// so it is unit-testable. Max attempts falls back to config default; "-" when no config.
/// </summary>
public static class StatusOverview
{
    public static IReadOnlyList<StatusRow> BuildRows(IReadOnlyList<ScannedCommand> commands, int? configMaxAttempts)
    {
        return commands.Select(scanned =>
        {
            if (scanned.Cmd is not { } cmd)
                return new StatusRow(scanned.Id, $"(malformed: {scanned.Error})", "!", "-", Warning: true);

            var max = cmd.MaxAttempts ?? configMaxAttempts;
            return new StatusRow(
                cmd.Id,
                cmd.Title,
                cmd.Status.ToYaml(),
                $"{cmd.Attempts}/{(max is null ? "-" : max.ToString())}",
                Warning: false);
        }).ToList();
    }
}
