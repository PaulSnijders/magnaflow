namespace MagnaFlow.WorkerController.Prompts;

/// <summary>
/// Command lifecycle (spec 002 data-model.md): draft -> ready -> running -> {questions ⇄ ready}
/// -> done, or running -> aborted (any terminal non-success — retries exhausted, human
/// abandonment, or an unrecoverable environment error). `draft` is human-authoring-only; the
/// controller never transitions into or out of it. Named CmdStatus (not CmdState) to mirror
/// v0.1's TaskState-not-TaskStatus naming rationale is moot here since there is no clashing
/// System.Threading.Tasks type for "Status".
/// </summary>
public enum CmdStatus
{
    Draft,
    Ready,
    Running,
    Questions,
    Done,
    Aborted,
}

public static class CmdStatusParser
{
    public static bool TryParse(string? value, out CmdStatus status)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "draft": status = CmdStatus.Draft; return true;
            case "ready": status = CmdStatus.Ready; return true;
            case "running": status = CmdStatus.Running; return true;
            case "questions": status = CmdStatus.Questions; return true;
            case "done": status = CmdStatus.Done; return true;
            case "aborted": status = CmdStatus.Aborted; return true;
            default: status = default; return false;
        }
    }

    public static string ToYaml(this CmdStatus status) => status switch
    {
        CmdStatus.Draft => "draft",
        CmdStatus.Ready => "ready",
        CmdStatus.Running => "running",
        CmdStatus.Questions => "questions",
        CmdStatus.Done => "done",
        CmdStatus.Aborted => "aborted",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
