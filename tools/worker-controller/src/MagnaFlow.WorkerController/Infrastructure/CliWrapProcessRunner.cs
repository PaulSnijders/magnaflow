using System.Runtime.InteropServices;
using System.Text;
using CliWrap;

namespace MagnaFlow.WorkerController.Infrastructure;

public sealed class CliWrapProcessRunner : IProcessRunner
{
    public Task<ProcessResult> RunExecutableAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onOutputLine = null,
        TimeSpan? timeout = null,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        var command = Cli.Wrap(executable).WithArguments(arguments);
        if (standardInput is not null)
            command = command.WithStandardInputPipe(PipeSource.FromString(standardInput));
        return ExecuteAsync(command, workingDirectory, onOutputLine, timeout, cancellationToken);
    }

    public Task<ProcessResult> RunShellAsync(
        string commandLine,
        string workingDirectory,
        Action<string>? onOutputLine = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // Windows: cmd.exe's /S-mode re-parsing needs the raw, unescaped command line. CliWrap's
        // array-form WithArguments applies its own argv-style escaping per element, which corrupts
        // embedded quotes exactly like ProcessStartInfo.ArgumentList does (see
        // docs/fase6-mf-run/argumentlist-audit-notes.md) — the single-string overload passes the
        // whole "/d /s /c ..." line through unescaped instead.
        // Unix: /bin/sh -c receives commandLine as one argv element with no textual re-parsing
        // layer, so there is nothing for CliWrap's escaping to corrupt.
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Cli.Wrap("cmd.exe").WithArguments($"/d /s /c \"{commandLine}\"")
            : Cli.Wrap("/bin/sh").WithArguments(["-c", commandLine]);
        return ExecuteAsync(command, workingDirectory, onOutputLine, timeout, cancellationToken);
    }

    private static async Task<ProcessResult> ExecuteAsync(
        Command command,
        string workingDirectory,
        Action<string>? onOutputLine,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        var gate = new object();

        void Emit(string line)
        {
            if (onOutputLine is null) return;
            lock (gate) onOutputLine(line);
        }

        command = command
            .WithWorkingDirectory(workingDirectory)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.Merge(
                PipeTarget.ToStringBuilder(stdOut),
                PipeTarget.ToDelegate(Emit)))
            .WithStandardErrorPipe(PipeTarget.Merge(
                PipeTarget.ToStringBuilder(stdErr),
                PipeTarget.ToDelegate(Emit)));

        using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : null;
        using var linked = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var result = await command.ExecuteAsync(linked.Token);
            return new ProcessResult(result.ExitCode, stdOut.ToString(), stdErr.ToString(), TimedOut: false);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            return new ProcessResult(-1, stdOut.ToString(), stdErr.ToString(), TimedOut: true);
        }
    }
}
