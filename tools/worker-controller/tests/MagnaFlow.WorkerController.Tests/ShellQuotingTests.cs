using MagnaFlow.WorkerController.Infrastructure;

namespace MagnaFlow.WorkerController.Tests;

/// <summary>
/// c0e1353:docs/fase6-mf-run/v0.1-completion-notes.md flagged that RunShellAsync's use of CliWrap's
/// WithArguments(string[]) on "cmd.exe /d /s /c &lt;commandLine&gt;" might exhibit the same quote
/// corruption mf-run hit with ProcessStartInfo.ArgumentList, if a configured build/test command
/// ever combines a quoted path (with spaces) with redirection. These tests exercise the real OS
/// shell (no fakes) against exactly that shape — TaskRunner's RunSequentiallyAsync forwards a
/// configured `command:` string to RunShellAsync unmodified, so testing the runner directly is
/// faithful to the real call site.
/// </summary>
public class ShellQuotingTests
{
    [Fact]
    public async Task RunShellAsync_quoted_path_with_spaces_plus_redirection_succeeds()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-specific cmd.exe /S-mode shape

        var runner = new CliWrapProcessRunner();
        using var temp = new TempProject();

        var toolDir = Path.Combine(temp.Root, "dir with spaces");
        Directory.CreateDirectory(toolDir);
        var toolPath = Path.Combine(toolDir, "tool.cmd");
        File.WriteAllText(toolPath, "@echo off\r\necho ran ok\r\n");
        var logPath = Path.Combine(temp.Root, "out with spaces.log");

        // The shape named in the completion notes / build prompt: a quoted path containing spaces
        // combined with shell redirection to another quoted path, e.g. a build `command:` of
        // `"C:\path with spaces\tool.exe" --flag > "out.log"`.
        var commandLine = $"\"{toolPath}\" --flag > \"{logPath}\" 2>&1";

        var result = await runner.RunShellAsync(commandLine, temp.Root, timeout: TimeSpan.FromSeconds(15));

        Assert.True(result.Succeeded, $"exit {result.ExitCode}\nstdout: {result.StdOut}\nstderr: {result.StdErr}");
        Assert.True(File.Exists(logPath), "expected redirected log file to be created");
        Assert.Contains("ran ok", File.ReadAllText(logPath));
    }

    [Fact]
    public async Task RunShellAsync_quoted_arg_containing_embedded_quotes_succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        var runner = new CliWrapProcessRunner();
        using var temp = new TempProject();

        // A configured command whose own arguments contain embedded quotes (e.g. a test runner
        // filter or a message argument), not just a quoted path.
        var commandLine = """powershell -NoProfile -Command "Write-Output 'build note: version "1.2.3"'" """.Trim();

        var result = await runner.RunShellAsync(commandLine, temp.Root, timeout: TimeSpan.FromSeconds(15));

        Assert.True(result.Succeeded, $"exit {result.ExitCode}\nstdout: {result.StdOut}\nstderr: {result.StdErr}");
        Assert.Contains("1.2.3", result.StdOut);
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
}
