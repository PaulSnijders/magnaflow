using System.ComponentModel;
using System.Text;
using CliWrap;

namespace MagnaFlow.MfCockpit.Infrastructure;

public sealed class CliWrapProcessRunner(ICockpitLog log) : IProcessRunner
{
    public async Task<ProcessResult> RunExecutableAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onOutputLine = null,
        TimeSpan? timeout = null,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        var gate = new object();

        void Emit(string line)
        {
            if (onOutputLine is null) return;
            lock (gate) onOutputLine(line);
        }

        var command = Cli.Wrap(executable)
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.Merge(
                PipeTarget.ToStringBuilder(stdOut),
                PipeTarget.ToDelegate(Emit)))
            .WithStandardErrorPipe(PipeTarget.Merge(
                PipeTarget.ToStringBuilder(stdErr),
                PipeTarget.ToDelegate(Emit)));

        if (standardInput is not null)
            command = command.WithStandardInputPipe(PipeSource.FromString(standardInput));

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
        catch (Win32Exception ex)
        {
            // The OS could not start the executable at all (missing file, not executable, ...) —
            // a configured cockpit.run.command/chat.command pointing at something bogus, not a bug
            // in the spawned tool. Report it through the normal ProcessResult shape (a reportable
            // failed run) instead of letting it become an unhandled 500 for every caller.
            var message = $"could not start '{executable}': {ex.Message}";
            log.Append($"spawn failed: {message}");
            return new ProcessResult(-1, stdOut.ToString(), message, TimedOut: false);
        }
    }
}
