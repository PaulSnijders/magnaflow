using MagnaFlow.WorkerController.Agents;
using MagnaFlow.WorkerController.Config;
using MagnaFlow.WorkerController.Infrastructure;

namespace MagnaFlow.WorkerController.Tests;

/// <summary>Disposable temp directory acting as a target project root.</summary>
public sealed class TempProject : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "mf-tests", Guid.NewGuid().ToString("N"));

    public TempProject() => Directory.CreateDirectory(Root);

    private string PromptsDir => Path.Combine(Root, "docs", "prompts");

    private static (string Num, string Name) SplitId(string id)
    {
        var numLen = id.Length > 4 && id[4] is >= 'B' and <= 'Z' ? 5 : 4;
        return (id[..numLen], id[(numLen + 1)..]);
    }

    public string WriteCmd(string id, string content) => WriteSibling(id, "cmd", content);
    public string WritePln(string id, string content) => WriteSibling(id, "pln", content);
    public string WriteQa(string id, string content) => WriteSibling(id, "qa", content);
    public string WriteRst(string id, string content) => WriteSibling(id, "rst", content);

    private string WriteSibling(string id, string kind, string content)
    {
        Directory.CreateDirectory(PromptsDir);
        var (num, name) = SplitId(id);
        var path = Path.Combine(PromptsDir, $"{num}-{kind}-{name}.md");
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public static string CmdMarkdown(
        string title = "Test command",
        string status = "ready",
        string? branch = "task/0001-test",
        string? baseBranch = null,
        string? group = null,
        bool? freshSession = null,
        string? resume = null,
        string[]? specs = null,
        int attempts = 0,
        int? maxAttempts = 3) =>
        $"""
        ---
        title: {title}
        status: {status}        # draft | ready | running | questions | done | aborted
        {(branch is null ? "" : $"branch: {branch}\n")}{(baseBranch is null ? "" : $"base: {baseBranch}\n")}{(group is null ? "" : $"group: {group}\n")}{(freshSession is null ? "" : $"fresh_session: {freshSession.ToString()!.ToLowerInvariant()}\n")}{(resume is null ? "" : $"resume: {resume}\n")}{(specs is null ? "" : "specs:\n" + string.Concat(specs.Select(s => $"  - {s}\n")))}attempts: {attempts}
        {(maxAttempts is null ? "" : $"max_attempts: {maxAttempts}\n")}created: 2026-07-08
        ---

        ## Goal
        Do something useful.

        ## Acceptance criteria
        - [ ] It works
        """;

    /// <summary>Directly seeds .magnaflow/&lt;id&gt;/session.yml — machine runtime evidence, as if a previous run had recorded it.</summary>
    public string EvidenceDirectory(string id) => Path.Combine(Root, ".magnaflow", id);

    public void WriteSessionEvidence(string id, string? sessionId)
    {
        var dir = EvidenceDirectory(id);
        Directory.CreateDirectory(dir);
        if (sessionId is not null)
            File.WriteAllText(Path.Combine(dir, "session.yml"), $"session: {sessionId}\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>The two reasons `git add` refuses an evidence path that GitClient tolerates.</summary>
public enum StageSkipReason
{
    /// <summary>The folder was never written (no session id, so no session.yml) — exit 128.</summary>
    NotOnDisk,
    /// <summary>The project's .gitignore excludes .magnaflow/ — exit 1.</summary>
    IgnoredByTheProject,
}

public sealed class FakeGitClient : IGitClient
{
    public bool Available = true;
    public bool Clean = true;
    public HashSet<string> Branches = ["main"];
    public string CurrentBranch = "main";
    public string? DefaultBranch = "main";
    public bool StagedWorkChanges = true;
    public List<string> Operations = [];

    /// <summary>What each commit carries: its message plus the paths staged into it. A branchless
    /// run that folds leaves exactly one commit holding both planes.</summary>
    public List<(string Message, List<string> Paths)> Commits = [];
    private readonly List<string> _staged = [];
    private int _commits;

    /// <summary>HEAD, moving on every commit — including one made behind the worker's back.</summary>
    public string Head { get; private set; } = "c0";

    /// <summary>True = every commit is already reachable from a remote-tracking ref, so amending
    /// one would be a force-push.</summary>
    public bool CommitsAreOnARemoteTrackingRef;

    /// <summary>Simulates something other than the worker committing mid-run (the agent, typically).</summary>
    public void CommitBehindTheWorkersBack() => Head = $"x{++_commits}";

    public Task<bool> IsAvailableAsync() => Task.FromResult(Available);
    public Task<bool> IsWorkingTreeCleanAsync() => Task.FromResult(Clean);
    public Task<string> GetCurrentBranchAsync() => Task.FromResult(CurrentBranch);
    public Task<string?> GetDefaultBranchAsync() => Task.FromResult(DefaultBranch);
    public Task<bool> BranchExistsAsync(string branch) => Task.FromResult(Branches.Contains(branch));

    public Task CheckoutAsync(string branch)
    {
        Operations.Add($"checkout {branch}");
        CurrentBranch = branch;
        return Task.CompletedTask;
    }

    public Task CreateBranchAsync(string branch, string fromBase)
    {
        Operations.Add($"create {branch} from {fromBase}");
        Branches.Add(branch);
        CurrentBranch = branch;
        return Task.CompletedTask;
    }

    /// <summary>Called at StagePathAsync time — lets tests observe what git would actually stage.</summary>
    public Action<string>? StageObserver;

    public Task StagePathAsync(string relativePath)
    {
        if (UnstageablePaths.TryGetValue(relativePath, out var refusal))
            throw new GitException(refusal == StageSkipReason.NotOnDisk
                ? $"git add -- {relativePath} failed (exit 128): fatal: pathspec '{relativePath}' did not match any files"
                : $"git add -- {relativePath} failed (exit 1): The following paths are ignored by one of your .gitignore files");
        Operations.Add($"stage {relativePath}");
        _staged.Add(relativePath);
        StageObserver?.Invoke(relativePath);
        return Task.CompletedTask;
    }

    /// <summary>Paths the real client would quietly skip, and why (see <see cref="StageSkipReason"/>).</summary>
    public Dictionary<string, StageSkipReason> UnstageablePaths = [];

    public Task StageIfPresentAsync(string relativePath)
    {
        if (UnstageablePaths.TryGetValue(relativePath, out var reason))
        {
            Operations.Add($"skip-stage {relativePath} ({reason})");
            return Task.CompletedTask;
        }
        return StagePathAsync(relativePath);
    }

    public Task StageAllExceptBookkeepingAsync()
    {
        Operations.Add("stage-all-except-bookkeeping");
        if (StagedWorkChanges)
            _staged.Add(WorkChanges);
        return Task.CompletedTask;
    }

    /// <summary>Stands in for whatever the agent changed, so tests can see it land in a commit.</summary>
    public const string WorkChanges = "<the agent's changes>";

    public Task<bool> HasStagedChangesAsync() => Task.FromResult(StagedWorkChanges);

    public Task CommitAsync(string message)
    {
        Operations.Add($"commit {message}");
        Head = $"c{++_commits}";
        Commits.Add((message, [.. _staged]));
        _staged.Clear();
        return Task.CompletedTask;
    }

    public Task<string> GetHeadCommitAsync() => Task.FromResult(Head);

    public Task<bool> IsOnRemoteTrackingRefAsync(string commit) =>
        Task.FromResult(CommitsAreOnARemoteTrackingRef);

    public Task AmendCommitAsync(string message)
    {
        Operations.Add($"amend {message}");
        Head = $"c{++_commits}";
        Commits[^1] = (message, [.. Commits[^1].Paths, .. _staged]);
        _staged.Clear();
        return Task.CompletedTask;
    }

    /// <summary>Null (default) = no remote configured, so nothing is ever pushed.</summary>
    public string? Remote;
    public bool PushFails;

    public Task<string?> GetRemoteAsync() => Task.FromResult(Remote);

    public Task PushAsync(string remote, string branch)
    {
        if (PushFails)
            throw new GitException($"push {remote} {branch} rejected");
        Operations.Add($"push {remote} {branch}");
        return Task.CompletedTask;
    }

    public IEnumerable<string> MutatingOperations =>
        Operations.Where(o => o.StartsWith("commit") || o.StartsWith("amend") || o.StartsWith("create") || o.StartsWith("stage") || o.StartsWith("checkout") || o.StartsWith("push"));
}

public sealed class FakeAgentRunner : IAgentRunner
{
    public bool Available = true;
    public Queue<AgentResult> Results = new();
    public List<string?> ResumeIds = [];
    public List<string> Prompts = [];
    private int _autoSession;

    /// <summary>Fires once, the next time the agent is invoked — lets a test simulate the agent
    /// doing something the worker did not, such as committing on its own.</summary>
    public Action? OnNextRun;

    public Task<bool> IsAvailableAsync(string workingDirectory) => Task.FromResult(Available);

    public Task<AgentResult> RunAsync(string prompt, string? resumeSessionId, string workingDirectory,
        Action<string> onRawOutputLine, CancellationToken cancellationToken = default)
    {
        Prompts.Add(prompt);
        ResumeIds.Add(resumeSessionId);
        var hook = OnNextRun;
        OnNextRun = null;
        hook?.Invoke();
        onRawOutputLine($"{{\"fake\":\"line {Prompts.Count}\"}}");
        var result = Results.Count > 0
            ? Results.Dequeue()
            : new AgentResult(0, $"session-{++_autoSession}", "Implemented the command.", TimedOut: false);
        return Task.FromResult(result);
    }
}

public sealed class FakeProcessRunner : IProcessRunner
{
    public Queue<ProcessResult> ShellResults = new();
    public List<string> ShellCommands = [];

    public List<(string Executable, IReadOnlyList<string> Arguments)> ExecutableCalls = [];
    public Queue<ProcessResult> ExecutableResults = new();

    /// <summary>Set to make the next RunExecutableAsync call throw instead of returning — simulates
    /// the configured executable not being found/spawnable at all (consumed after one throw).</summary>
    public Exception? NextExecutableThrows;

    /// <summary>Fires on every RunExecutableAsync call, before its result — lets a test see what the
    /// rest of the world (git, files) looked like at that moment.</summary>
    public Action<IReadOnlyList<string>>? OnExecutable;

    public Task<ProcessResult> RunExecutableAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, Action<string>? onOutputLine = null, TimeSpan? timeout = null,
        string? standardInput = null, CancellationToken cancellationToken = default)
    {
        ExecutableCalls.Add((executable, arguments));
        OnExecutable?.Invoke(arguments);
        if (NextExecutableThrows is { } ex)
        {
            NextExecutableThrows = null;
            throw ex;
        }
        var result = ExecutableResults.Count > 0 ? ExecutableResults.Dequeue() : new ProcessResult(0, "", "", false);
        return Task.FromResult(result);
    }

    public Task<ProcessResult> RunShellAsync(string commandLine, string workingDirectory,
        Action<string>? onOutputLine = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ShellCommands.Add(commandLine);
        var result = ShellResults.Count > 0 ? ShellResults.Dequeue() : new ProcessResult(0, "ok", "", false);
        onOutputLine?.Invoke(result.StdOut);
        return Task.FromResult(result);
    }
}

public static class TestConfig
{
    public static ProjectConfig Create(
        int maxAttempts = 3,
        IReadOnlyList<string>? buildCommands = null,
        IReadOnlyList<string>? testCommands = null,
        bool hasRunServices = false,
        string runCommand = "mf-run",
        bool hasRunStable = false) => new()
    {
        BuildCommands = buildCommands ?? ["build-cmd"],
        TestCommands = testCommands ?? ["test-cmd"],
        MaxAttempts = maxAttempts,
        CommandTimeout = TimeSpan.FromMinutes(1),
        AgentCommand = "claude",
        AgentArgs = [],
        HasRunServices = hasRunServices,
        HasRunStable = hasRunStable,
        RunCommand = runCommand,
    };
}
