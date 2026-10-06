using System.Diagnostics;
using System.Globalization;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Watch;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>Disposable temp directory acting as a target project root — same role as
/// MagnaFlow.MfWatch.Tests' TempProject.</summary>
public sealed class TempProject : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N"));

    public TempProject() => Directory.CreateDirectory(Root);

    public string PromptsDir => Path.Combine(Root, "docs", "prompts");
    public string SpecsDir => Path.Combine(Root, "docs", "specs");
    public string MagnaflowDir => Path.Combine(Root, ".magnaflow");

    public string WriteCmd(string id, string status = "ready", string title = "Test command", string body = "## Goal\nDo something useful.\n")
    {
        Directory.CreateDirectory(PromptsDir);
        var (num, name) = SplitId(id);
        var path = Path.Combine(PromptsDir, $"{num}-cmd-{name}.md");
        File.WriteAllText(path, CmdMarkdown(status, title, body));
        return path;
    }

    public string WriteSibling(string id, string kind, string content)
    {
        Directory.CreateDirectory(PromptsDir);
        var (num, name) = SplitId(id);
        var path = Path.Combine(PromptsDir, $"{num}-{kind}-{name}.md");
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteEvidence(string id, string fileName, string content)
    {
        var dir = Path.Combine(MagnaflowDir, id);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteSpec(string relativePath, string content)
    {
        var path = Path.Combine(SpecsDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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

    /// <summary>Writes .magnaflow/mf-watch.lock in mf-watch's own two-line format (PID, round-trip
    /// ISO-8601 start time) — MagnaFlow.MfWatch.Runtime.InstanceLock's shape, duplicated here so
    /// Watch/MfWatchLockFile.cs's reader can be tested without a real mf-watch process.</summary>
    public string WriteWatchLock(int pid, DateTimeOffset startTimeUtc) =>
        WriteFile(".magnaflow/mf-watch.lock", $"{pid}\n{startTimeUtc.ToString("o", CultureInfo.InvariantCulture)}\n");

    public string CmdPath(string id)
    {
        var (num, name) = SplitId(id);
        return Path.Combine(PromptsDir, $"{num}-cmd-{name}.md");
    }

    public static (string Num, string Name) SplitId(string id)
    {
        var numLen = id.Length > 4 && id[4] is >= 'B' and <= 'Z' ? 5 : 4;
        return (id[..numLen], id[(numLen + 1)..]);
    }

    public static string CmdMarkdown(string status = "ready", string title = "Test command", string body = "## Goal\nDo something useful.\n") =>
        $"""
        ---
        title: {title}
        status: {status}
        attempts: 0
        ---

        {body}
        """;

    /// <summary>Initializes a real git repo and commits everything currently on disk — the
    /// integration tests need a real repo (ontwerp-v0.1.md's own writes shell out to real git).</summary>
    public void InitGit()
    {
        if (!Directory.EnumerateFileSystemEntries(Root).Any())
            File.WriteAllText(Path.Combine(Root, ".gitkeep"), ""); // git commit needs something to commit

        Git(Root, "init", "-q", "-b", "main");
        Git(Root, "config", "user.email", "test@example.com");
        Git(Root, "config", "user.name", "test");
        Git(Root, "add", "-A");
        Git(Root, "commit", "-q", "-m", "seed");
    }

    public static void Git(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {process.StandardError.ReadToEnd()}");
    }

    public IReadOnlyList<string> GitLog() => GitLogAt(Root);

    /// <summary>Same as GitLog(), but for a directory that isn't this TempProject's own Root — used
    /// by write #6's tests, which scaffold a brand-new directory under a separate new_project.root
    /// rather than reusing the fixture's own working copy.</summary>
    public static IReadOnlyList<string> GitLogAt(string root)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("log");
        psi.ArgumentList.Add("--oneline");
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
}

public sealed record CommitCall(string ProjectRoot, string RelativePath, string Message);

public sealed class FakeGitClient : IGitClient
{
    public GitInfo InfoToReturn = new(true, "main", false, [], []);
    public List<CommitCall> Commits { get; } = [];
    public List<(string ProjectRoot, string Message)> CommitAllCalls { get; } = [];
    public bool ThrowOnCommit;
    public bool CommitAllResult = true;
    public GitCommandResult? PushResult = new(0, "", false);
    public List<string> PushCalls { get; } = [];
    public GitCommandResult PullResult = new(0, "Already up to date.", false);
    public List<string> PullCalls { get; } = [];
    public GitCommandResult FetchResult = new(0, "", false);
    public List<string> FetchCalls { get; } = [];
    public GitBranches BranchesToReturn = new("main", [new GitBranch("main", false)]);
    public GitCommandResult SwitchResult = new(0, "", false);
    public List<(string ProjectRoot, string Branch)> SwitchCalls { get; } = [];
    /// <summary>How often the (subprocess-spawning) info read was actually reached — what the
    /// GET .../git TTL cache is there to keep down (docs/prompts/0010).</summary>
    public int InfoCalls { get; private set; }

    public Task<GitInfo> GetInfoAsync(string projectRoot, int logCount = 10)
    {
        InfoCalls++;
        return Task.FromResult(InfoToReturn);
    }

    public Task CommitFileAsync(string projectRoot, string relativePath, string message)
    {
        if (ThrowOnCommit)
            throw new GitException("simulated commit failure");
        Commits.Add(new CommitCall(projectRoot, relativePath, message));
        return Task.CompletedTask;
    }

    public Task<bool> CommitAllAsync(string projectRoot, string message)
    {
        if (ThrowOnCommit)
            throw new GitException("simulated commit failure");
        CommitAllCalls.Add((projectRoot, message));
        return Task.FromResult(CommitAllResult);
    }

    public Task<GitCommandResult?> PushAsync(string projectRoot)
    {
        PushCalls.Add(projectRoot);
        return Task.FromResult(PushResult);
    }

    public Task<GitCommandResult> PullFastForwardAsync(string projectRoot)
    {
        PullCalls.Add(projectRoot);
        return Task.FromResult(PullResult);
    }

    public Task<GitCommandResult> FetchAsync(string projectRoot)
    {
        FetchCalls.Add(projectRoot);
        return Task.FromResult(FetchResult);
    }

    public Task<GitBranches> ListBranchesAsync(string projectRoot) => Task.FromResult(BranchesToReturn);

    public Task<GitCommandResult> SwitchAsync(string projectRoot, string branch)
    {
        SwitchCalls.Add((projectRoot, branch));
        return Task.FromResult(SwitchResult);
    }

    public GitSyncResult SyncResult = new(new GitCommandResult(0, "", false), [], new GitCommandResult(0, "", false));
    public List<string> SyncCalls { get; } = [];

    public Task<GitSyncResult> SyncRebaseAsync(string projectRoot)
    {
        SyncCalls.Add(projectRoot);
        return Task.FromResult(SyncResult);
    }
}

/// <summary>Swaps IRunClient for endpoint-wiring tests that have no business spawning a real
/// process — RunApiIntegrationTests uses this; the small number of scenarios that must exercise a
/// real spawn (start output, timeout kill) use a real mf-run.command stub instead (RunE2ETests).</summary>
public sealed class FakeRunClient : IRunClient
{
    public string StatusOutput = "[]";
    public int StatusExitCode;
    public bool StatusTimedOut;
    /// <summary>How often the mf-run status spawn was actually reached — see FakeGitClient.InfoCalls.</summary>
    public int StatusCalls { get; private set; }
    public List<(string ProjectRoot, string? Service)> StartCalls { get; } = [];
    public List<(string ProjectRoot, string? Service)> StopCalls { get; } = [];
    public List<(string ProjectRoot, string? Service)> RestartCalls { get; } = [];

    public Task<RunActionResult> StatusAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        return Task.FromResult(new RunActionResult(StatusExitCode, StatusOutput, StatusTimedOut));
    }

    public Task<RunActionResult> StartAsync(string projectRoot, string? service, CancellationToken cancellationToken = default)
    {
        StartCalls.Add((projectRoot, service));
        return Task.FromResult(new RunActionResult(0, $"{service ?? "all"}: started", false));
    }

    public Task<RunActionResult> StopAsync(string projectRoot, string? service, CancellationToken cancellationToken = default)
    {
        StopCalls.Add((projectRoot, service));
        return Task.FromResult(new RunActionResult(0, $"{service ?? "all"}: stopped", false));
    }

    public Task<RunActionResult> RestartAsync(string projectRoot, string? service, CancellationToken cancellationToken = default)
    {
        RestartCalls.Add((projectRoot, service));
        return Task.FromResult(new RunActionResult(0, $"{service ?? "all"}: restarted", false));
    }
}

/// <summary>Swaps IWatchControl for endpoint-wiring tests — same role as FakeRunClient.</summary>
public sealed class FakeWatchControl : IWatchControl
{
    public bool RunningResult;
    public WatchControlResult StartResult = new(true, null);
    public WatchControlResult StopResult = new(true, null);
    public List<string> StartCalls { get; } = [];
    public List<string> StopCalls { get; } = [];

    public Task<bool> IsRunningAsync(string projectRoot, CancellationToken cancellationToken = default) =>
        Task.FromResult(RunningResult);

    public Task<WatchControlResult> StartAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        StartCalls.Add(projectRoot);
        return Task.FromResult(StartResult);
    }

    public Task<WatchControlResult> StopAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        StopCalls.Add(projectRoot);
        return Task.FromResult(StopResult);
    }
}

/// <summary>Fake OS process table for WindowsWatchControl's spawner seam — same role as mf-run's
/// own FakeProcessSpawner (tests mutate Processes directly to simulate a live/dead/reused PID).</summary>
public sealed class FakeWatchProcessSpawner : IWatchProcessSpawner
{
    public Dictionary<int, WatchProcessSnapshot> Processes { get; } = [];
    public int NextPid = 1000;
    public List<(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory)> StartCalls { get; } = [];
    public List<int> KillCalls { get; } = [];
    public DateTimeOffset SpawnedStartTime = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    public int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        StartCalls.Add((executable, arguments, workingDirectory));
        var pid = NextPid++;
        Processes[pid] = new WatchProcessSnapshot(pid, SpawnedStartTime);
        return pid;
    }

    public WatchProcessSnapshot? GetProcess(int pid) => Processes.GetValueOrDefault(pid);

    public void Kill(int pid)
    {
        KillCalls.Add(pid);
        Processes.Remove(pid);
    }
}

public sealed class FakeCockpitLog : ICockpitLog
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mf-cockpit-tests", "fake.log");
    public List<string> Messages { get; } = [];

    public void Append(string message) => Messages.Add(message);
}

/// <summary>Runs a delegate instead of a real process — same role as MagnaFlow.MfWatch.Tests'
/// FakeProcessRunner.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string Executable, IReadOnlyList<string> Arguments, string? StandardInput)> Calls = [];
    public Func<string, IReadOnlyList<string>, string?, Action<string>?, ProcessResult>? OnRun;
    public Queue<ProcessResult> Results = new();

    public Task<ProcessResult> RunExecutableAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onOutputLine = null,
        TimeSpan? timeout = null,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((executable, arguments, standardInput));
        var result = OnRun?.Invoke(executable, arguments, standardInput, onOutputLine)
            ?? (Results.Count > 0 ? Results.Dequeue() : new ProcessResult(0, "", "", false));
        return Task.FromResult(result);
    }
}
