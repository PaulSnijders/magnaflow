using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MagnaFlow.MfRun.Infrastructure;

/// <summary>
/// Real OS process spawning. Redirection to the log file happens at the platform shell's level
/// (cmd.exe's <c>&gt;</c>, or <c>exec</c> under <c>/bin/sh</c> on Unix) rather than via a .NET pipe:
/// a piped redirect requires mf-run's own process to keep pumping the pipe for as long as the
/// service runs, which defeats "detached, outlives mf-run" (the pipe breaks — and the service can
/// hang or crash writing to it — the moment mf-run exits). Shell-level redirection has no such
/// requirement: once the shell process is started, mf-run's own process is irrelevant to it.
/// </summary>
public sealed class SystemProcessSpawner : IProcessSpawner
{
    public int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, string logPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var redirected = BuildRedirectedCommandLine(executable, arguments, logPath, isWindows);

        // Windows: ProcessStartInfo.Arguments is used raw, not re-escaped the way ArgumentList would
        // be — cmd.exe re-parses its /C payload as a single string with its own quoting rules, and
        // .NET's normal ArgumentList escaping (meant for argv-style children) corrupts embedded
        // quotes in that string (each `"` becomes `\"`, which cmd.exe reads as a literal backslash
        // rather than an escaped quote, breaking the redirect target's filename). Wrapping the
        // whole payload in one outer pair of quotes plus `/S` is the documented safe form: cmd
        // strips exactly that outer pair and runs the rest verbatim.
        // Unix: /bin/sh receives argv directly (no command-line string to re-parse), so
        // ArgumentList's per-argument escaping never applies here and is safe as-is.
        var startInfo = isWindows
            ? new ProcessStartInfo("cmd.exe") { Arguments = $"/d /s /c \"{redirected}\"" }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", redirected } };

        startInfo.WorkingDirectory = workingDirectory;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = false;
        startInfo.RedirectStandardError = false;
        startInfo.RedirectStandardInput = false;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start '{executable}'");
        return process.Id;
    }

    public ProcessSnapshot? GetProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return new ProcessSnapshot(pid, process.StartTime.ToUniversalTime());
        }
        catch (ArgumentException)
        {
            return null; // no process with this PID
        }
        catch (InvalidOperationException)
        {
            return null; // exited between GetProcessById and reading StartTime
        }
        catch (Win32Exception)
        {
            return null; // e.g. access denied querying a process we don't own
        }
    }

    public void KillTree(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // already gone — killing nothing is the idempotent, expected outcome
        }
        catch (InvalidOperationException)
        {
            // exited between GetProcessById and Kill
        }
    }

    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, string? logPath = null, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        StreamWriter? log = null;
        if (logPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
            log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        var output = new StringBuilder();
        var gate = new object();
        void OnLine(string? line)
        {
            if (line is null) return;
            lock (gate)
            {
                if (log is not null) log.WriteLine(line);
                else output.AppendLine(line);
            }
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => OnLine(e.Data);
            process.ErrorDataReceived += (_, e) => OnLine(e.Data);
            if (!process.Start())
                throw new InvalidOperationException($"failed to start '{executable}'");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = timeout is null ? new CancellationTokenSource() : new CancellationTokenSource(timeout.Value);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* exited meanwhile */ }
                // Bounded: a grandchild that escaped the tree could hold the output pipe open forever.
                using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(drain.Token); } catch (OperationCanceledException) { /* give up on the tail */ }
                lock (gate) return new CommandResult(-1, output.ToString(), TimedOut: true);
            }
            // WaitForExitAsync also waits for the redirected streams' EOF, so the output is complete here.
            lock (gate) return new CommandResult(process.ExitCode, output.ToString(), TimedOut: false);
        }
        finally
        {
            log?.Dispose();
        }
    }

    /// <summary>Builds the shell command line that runs <paramref name="executable"/> with stdout+stderr
    /// merged into <paramref name="logPath"/> (created/truncated by the redirection itself).</summary>
    private static string BuildRedirectedCommandLine(string executable, IReadOnlyList<string> arguments, string logPath, bool isWindows)
    {
        var command = new StringBuilder();
        if (!isWindows)
            command.Append("exec ");

        command.Append(Quote(executable));
        foreach (var arg in arguments)
        {
            command.Append(' ');
            command.Append(Quote(arg));
        }

        command.Append(" > ").Append(Quote(logPath)).Append(" 2>&1");
        return command.ToString();
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
