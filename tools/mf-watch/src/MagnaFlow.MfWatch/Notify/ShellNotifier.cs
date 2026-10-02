using MagnaFlow.MfWatch.Infrastructure;

namespace MagnaFlow.MfWatch.Notify;

/// <summary>
/// Cross-platform without platform code (ontwerp-v0.1.md "Notifications"): notify_command is a
/// shell command template with {title}/{message} placeholders (PowerShell toast, notify-send,
/// osascript — whatever the human configured). A missing notify_command means this channel is
/// simply off; a failing one is swallowed (best-effort — console output already happened).
/// </summary>
public sealed class ShellNotifier(IProcessRunner processes, string? commandTemplate, string workingDirectory, Action<string> log) : INotifier
{
    public async Task NotifyAsync(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(commandTemplate))
            return;

        var commandLine = commandTemplate.Replace("{title}", title).Replace("{message}", message);
        try
        {
            var result = await processes.RunShellAsync(commandLine, workingDirectory, timeout: TimeSpan.FromSeconds(30));
            if (!result.Succeeded)
                log($"notify_command exited {result.ExitCode}: {result.StdErr.Trim()}");
        }
        catch (Exception ex)
        {
            log($"notify_command failed to run: {ex.Message}");
        }
    }
}
