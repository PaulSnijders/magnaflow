using MagnaFlow.MfWatch.Infrastructure;
using MagnaFlow.MfWatch.Notify;

namespace MagnaFlow.MfWatch.Tests;

/// <summary>Disposable temp directory acting as a target project root.</summary>
public sealed class TempProject : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "mf-watch-tests", Guid.NewGuid().ToString("N"));

    public TempProject() => Directory.CreateDirectory(Root);

    private string PromptsDir => Path.Combine(Root, "docs", "prompts");

    private static (string Num, string Name) SplitId(string id)
    {
        var numLen = id.Length > 4 && id[4] is >= 'B' and <= 'Z' ? 5 : 4;
        return (id[..numLen], id[(numLen + 1)..]);
    }

    public string WriteCmd(string id, string status = "ready", string title = "Test command")
    {
        Directory.CreateDirectory(PromptsDir);
        var (num, name) = SplitId(id);
        var path = Path.Combine(PromptsDir, $"{num}-cmd-{name}.md");
        File.WriteAllText(path, CmdMarkdown(status, title));
        return path;
    }

    public string CmdPath(string id)
    {
        var (num, name) = SplitId(id);
        return Path.Combine(PromptsDir, $"{num}-cmd-{name}.md");
    }

    public string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public static string CmdMarkdown(string status = "ready", string title = "Test command") =>
        $"""
        ---
        title: {title}
        status: {status}
        attempts: 0
        ---

        ## Goal
        Do something useful.
        """;

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

public sealed class FakeGitClient : IGitClient
{
    public bool Available = true;
    public Queue<bool> PullResults = new();
    public bool PullFails;
    /// <summary>Non-null: every pull throws a conflict on these files (the rebase already aborted).</summary>
    public string[]? PullConflict;
    public bool PushFails;
    /// <summary>Consumed one per push; empty means Pushed.</summary>
    public Queue<PushOutcome> PushOutcomes = new();
    public string SyncState = "local1..remote1";
    public List<string> Operations = [];

    public Task<bool> IsAvailableAsync() => Task.FromResult(Available);

    public Task<bool> PullAsync()
    {
        if (PullConflict is not null)
        {
            Operations.Add("pull (conflict)");
            throw new GitConflictException(PullConflict, "conflict");
        }
        if (PullFails)
        {
            Operations.Add("pull (failed)");
            throw new GitException("pull rejected");
        }
        var pulled = PullResults.Count > 0 && PullResults.Dequeue();
        Operations.Add($"pull (brought-commits={pulled})");
        return Task.FromResult(pulled);
    }

    public Task<PushOutcome> PushAsync()
    {
        if (PushFails)
            throw new GitException("push rejected");
        var outcome = PushOutcomes.Count > 0 ? PushOutcomes.Dequeue() : PushOutcome.Pushed;
        Operations.Add(outcome == PushOutcome.Pushed ? "push" : "push (rejected)");
        return Task.FromResult(outcome);
    }

    public Task<string> SyncStateAsync() => Task.FromResult(SyncState);
}

/// <summary>Runs a delegate instead of a real process, so tests can simulate the worker mutating
/// the cmd file's status the way a real mf-worker run would.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string Executable, IReadOnlyList<string> Arguments)> ExecutableCalls = [];
    public List<CancellationToken> ExecutableTokens = [];
    public Func<string, IReadOnlyList<string>, ProcessResult>? OnRunExecutable;
    public Queue<ProcessResult> ExecutableResults = new();

    public List<string> ShellCommands = [];
    public Queue<ProcessResult> ShellResults = new();

    public Task<ProcessResult> RunExecutableAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, Action<string>? onOutputLine = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ExecutableCalls.Add((executable, arguments));
        ExecutableTokens.Add(cancellationToken);
        var result = OnRunExecutable?.Invoke(executable, arguments)
            ?? (ExecutableResults.Count > 0 ? ExecutableResults.Dequeue() : new ProcessResult(0, "", "", false));
        return Task.FromResult(result);
    }

    public Task<ProcessResult> RunShellAsync(string commandLine, string workingDirectory,
        Action<string>? onOutputLine = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ShellCommands.Add(commandLine);
        var result = ShellResults.Count > 0 ? ShellResults.Dequeue() : new ProcessResult(0, "", "", false);
        return Task.FromResult(result);
    }
}

public sealed class FakeNotifier : INotifier
{
    public List<(string Title, string Message)> Notifications = [];

    public Task NotifyAsync(string title, string message)
    {
        Notifications.Add((title, message));
        return Task.CompletedTask;
    }
}
