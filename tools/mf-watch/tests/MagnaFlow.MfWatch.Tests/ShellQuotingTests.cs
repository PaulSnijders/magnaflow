using MagnaFlow.MfWatch.Infrastructure;
using MagnaFlow.MfWatch.Notify;

namespace MagnaFlow.MfWatch.Tests;

/// <summary>
/// c0e1353:docs/fase6-mf-run/v0.1-completion-notes.md flagged that RunShellAsync's use of CliWrap's
/// WithArguments(string[]) on "cmd.exe /d /s /c &lt;commandLine&gt;" might exhibit the same quote
/// corruption mf-run hit with ProcessStartInfo.ArgumentList, if a configured notify_command ever
/// combines embedded quotes with spaces. These tests exercise the real OS shell (no fakes) to
/// settle whether CliWrap's own argument escaping actually reproduces it.
/// </summary>
public class ShellQuotingTests
{
    [Fact]
    public async Task RunShellAsync_quoted_arg_containing_embedded_quotes_and_spaces_succeeds()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-specific cmd.exe /S-mode shape

        var runner = new CliWrapProcessRunner();
        using var temp = new TempProject();

        // Mirrors a notify_command template after {message} substitution: a message containing an
        // embedded quote, inside an already-quoted PowerShell -Command argument.
        var message = """He said "hi" today""";
        var commandLine = $"""powershell -NoProfile -Command "Write-Output '{message}'" """.Trim();

        var result = await runner.RunShellAsync(commandLine, temp.Root, timeout: TimeSpan.FromSeconds(15));

        Assert.True(result.Succeeded, $"exit {result.ExitCode}\nstdout: {result.StdOut}\nstderr: {result.StdErr}");
        Assert.Contains("hi", result.StdOut);
    }

    [Fact]
    public async Task RunShellAsync_quoted_path_with_spaces_plus_redirection_succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        var runner = new CliWrapProcessRunner();
        using var temp = new TempProject();

        var toolDir = Path.Combine(temp.Root, "dir with spaces");
        Directory.CreateDirectory(toolDir);
        var toolPath = Path.Combine(toolDir, "tool.cmd");
        File.WriteAllText(toolPath, "@echo off\r\necho ran ok\r\n");
        var logPath = Path.Combine(temp.Root, "out with spaces.log");

        // The shape flagged in the completion notes: a quoted path containing spaces combined with
        // shell redirection to another quoted path, all inside one cmd /d /s /c command line.
        var commandLine = $"\"{toolPath}\" > \"{logPath}\" 2>&1";

        var result = await runner.RunShellAsync(commandLine, temp.Root, timeout: TimeSpan.FromSeconds(15));

        Assert.True(result.Succeeded, $"exit {result.ExitCode}\nstdout: {result.StdOut}\nstderr: {result.StdErr}");
        Assert.True(File.Exists(logPath), "expected redirected log file to be created");
        Assert.Contains("ran ok", File.ReadAllText(logPath));
    }

    [Fact]
    public async Task RunShellAsync_unix_quoted_path_with_spaces_plus_redirection_would_succeed()
    {
        if (OperatingSystem.IsWindows()) return; // exercised only on an actual Unix runner

        var runner = new CliWrapProcessRunner();
        using var temp = new TempProject();

        var toolDir = Path.Combine(temp.Root, "dir with spaces");
        Directory.CreateDirectory(toolDir);
        var toolPath = Path.Combine(toolDir, "tool.sh");
        File.WriteAllText(toolPath, "#!/bin/sh\necho ran ok\n");
        var logPath = Path.Combine(temp.Root, "out with spaces.log");

        var commandLine = $"sh \"{toolPath}\" > \"{logPath}\" 2>&1";

        var result = await runner.RunShellAsync(commandLine, temp.Root, timeout: TimeSpan.FromSeconds(15));

        Assert.True(result.Succeeded, $"exit {result.ExitCode}\nstdout: {result.StdOut}\nstderr: {result.StdErr}");
        Assert.True(File.Exists(logPath), "expected redirected log file to be created");
        Assert.Contains("ran ok", File.ReadAllText(logPath));
    }

    [Fact]
    public async Task ShellNotifier_notify_command_with_embedded_quote_in_message_does_not_crash()
    {
        if (!OperatingSystem.IsWindows()) return;

        var runner = new CliWrapProcessRunner();
        using var temp = new TempProject();
        var logs = new List<string>();

        var notifier = new ShellNotifier(
            runner,
            """powershell -NoProfile -Command "Write-Output '{message}'" """.Trim(),
            temp.Root,
            logs.Add);

        await notifier.NotifyAsync("Build failed", """commit message with an embedded "quote" in it""");

        Assert.Empty(logs); // a non-zero exit or exception would have logged something
    }
}
