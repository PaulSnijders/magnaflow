namespace MagnaFlow.MfCockpit.Prompts;

/// <summary>
/// Command lifecycle as the cockpit knows it (docs/mf-spec/system.md, docs/fase2-worker-controller
/// /v0.2-completion-notes.md): draft | ready | running | questions | done | aborted. Own copy, not
/// a reference to MagnaFlow.WorkerController — the cockpit knows frontmatter strings and file
/// layouts, nothing of the controller's internal types (ontwerp-v0.1.md "Tech").
/// </summary>
public enum CmdStatus
{
    Draft,
    Ready,
    Running,
    Questions,
    Done,
    Aborted,
    /// <summary>Frontmatter present but status: missing/unrecognized — shown, never hidden.</summary>
    Unknown,
}

public static class CmdStatusParser
{
    public static CmdStatus Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "draft" => CmdStatus.Draft,
        "ready" => CmdStatus.Ready,
        "running" => CmdStatus.Running,
        "questions" => CmdStatus.Questions,
        "done" => CmdStatus.Done,
        "aborted" => CmdStatus.Aborted,
        _ => CmdStatus.Unknown,
    };

    public static string ToYaml(this CmdStatus status) => status switch
    {
        CmdStatus.Draft => "draft",
        CmdStatus.Ready => "ready",
        CmdStatus.Running => "running",
        CmdStatus.Questions => "questions",
        CmdStatus.Done => "done",
        CmdStatus.Aborted => "aborted",
        _ => "unknown",
    };
}
