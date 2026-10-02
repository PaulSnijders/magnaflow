using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Api;

internal static class ProjectResolver
{
    public static ProjectEntry? Find(ProjectRegistry registry, string name) => registry.Find(name);

    /// <summary>Whether any command in the project is currently `running` — the guard shared by the
    /// remove-project (write #7) and git-pull (write #8) endpoints, both of which must refuse (409)
    /// mid-run so they can't hide or disrupt a live worker (ontwerp-v0.5.md items 4 and 7).</summary>
    public static bool HasRunningCommand(ProjectEntry project) =>
        Directory.Exists(project.Path) && LaneScanner.Scan(project.Path).Any(i => i.Cmd?.Status == CmdStatus.Running);
}
