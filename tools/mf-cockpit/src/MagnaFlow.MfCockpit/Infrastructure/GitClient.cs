namespace MagnaFlow.MfCockpit.Infrastructure;

public sealed class GitClient(IProcessRunner processRunner) : IGitClient
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    // A pull or a fetch reaches the network; give those more room than a local status/log, but
    // still a hard cap so a stuck fetch (or a credentials prompt that GIT_TERMINAL_PROMPT=0 turned
    // into a hang on a stale git) fails fast instead of wedging the request (ontwerp-v0.5.md item
    // 7). Everything in write #10 except the fetch is local and stays on GitTimeout.
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(60);
    private const string RecordEnd = "<<mf-cockpit-record-end>>";

    public async Task<GitInfo> GetInfoAsync(string projectRoot, int logCount = 10)
    {
        var version = await RunAsync(projectRoot, "--version");
        if (!version.Succeeded)
            return new GitInfo(false, null, false, [], []);

        var branchResult = await RunAsync(projectRoot, "rev-parse", "--abbrev-ref", "HEAD");
        var branch = branchResult.Succeeded ? branchResult.StdOut.Trim() : null;

        // --untracked-files=all: without it, a wholly-new directory ("docs/specs/frontend/" not
        // yet tracked at all) collapses to one "?? docs/" line instead of listing the file(s)
        // inside — which would silently break both the dirty-file list and the "default commit
        // message is the one changed spec file" suggestion for the (very common) case of a new
        // spec file. A conscious tradeoff: slower on repos with huge untracked trees, but this is
        // a single local project the operator is actively looking at, not a CI scan.
        var statusResult = await RunAsync(projectRoot, "status", "--porcelain", "--untracked-files=all");
        var changedFiles = statusResult.Succeeded ? ParseChangedFiles(statusResult.StdOut) : [];
        var dirty = changedFiles.Count > 0;

        // --pretty=tformat:, not format: — empirically, `format:` hangs indefinitely under this
        // process-spawning setup on Windows (git.exe never signals completion even though its
        // output is already fully written and captured), while the otherwise-equivalent
        // `tformat:` returns immediately. tformat: is also git's own documented choice for
        // scripted/programmatic use.
        var logResult = await RunAsync(projectRoot, "log", $"-n{logCount}", "--date=iso-strict", $"--pretty=tformat:%H%n%an%n%ad%n%s%n{RecordEnd}");
        var commits = new List<GitCommit>();
        if (logResult.Succeeded)
        {
            foreach (var entry in logResult.StdOut.Split(RecordEnd, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                    commits.Add(new GitCommit(parts[0], parts[1], parts[2], parts[3]));
            }
        }

        // Local only: against the remote-tracking ref as last fetched, so a divergence shows after
        // the card's ↻ (or a refused ff-only pull, which fetches too). No upstream: both null.
        int? ahead = null, behind = null;
        var counts = await RunAsync(projectRoot, "rev-list", "--left-right", "--count", "HEAD...@{u}");
        var countParts = counts.Succeeded ? counts.StdOut.Split('\t', StringSplitOptions.TrimEntries) : [];
        if (countParts.Length == 2 && int.TryParse(countParts[0], out var a) && int.TryParse(countParts[1], out var b))
            (ahead, behind) = (a, b);

        return new GitInfo(true, branch, dirty, commits, changedFiles, ahead, behind);
    }

    public async Task CommitFileAsync(string projectRoot, string relativePath, string message)
    {
        var add = await RunAsync(projectRoot, "add", "--", relativePath);
        if (!add.Succeeded)
            throw new GitException($"git add {relativePath} failed: {add.StdErr.Trim()}");

        var commit = await RunAsync(projectRoot, "commit", "--quiet", "-m", message, "--", relativePath);
        if (!commit.Succeeded)
            throw new GitException($"git commit failed: {commit.StdErr.Trim()}");
    }

    public async Task<bool> CommitAllAsync(string projectRoot, string message)
    {
        var status = await RunAsync(projectRoot, "status", "--porcelain");
        if (!status.Succeeded || string.IsNullOrWhiteSpace(status.StdOut))
            return false;

        var add = await RunAsync(projectRoot, "add", "-A");
        if (!add.Succeeded)
            throw new GitException($"git add -A failed: {add.StdErr.Trim()}");

        var commit = await RunAsync(projectRoot, "commit", "--quiet", "-m", message);
        if (!commit.Succeeded)
            throw new GitException($"git commit failed: {commit.StdErr.Trim()}");

        return true;
    }

    public async Task<GitCommandResult?> PushAsync(string projectRoot)
    {
        var remotes = await RunAsync(projectRoot, "remote");
        if (!remotes.Succeeded || string.IsNullOrWhiteSpace(remotes.StdOut))
            return null;

        // Same GIT_TERMINAL_PROMPT=0 + NetworkTimeout reasoning as the pull below.
        var result = await processRunner.RunExecutableAsync(
            "git", ["--no-pager", "push", "--quiet"], projectRoot, timeout: NetworkTimeout);
        return Merged(result);
    }

    public async Task<GitCommandResult> PullFastForwardAsync(string projectRoot)
    {
        // GIT_TERMINAL_PROMPT=0 is also set process-wide at startup (Program.cs); the whole point is
        // a credentials prompt must fail fast, not block on a hidden console. NetworkTimeout is the
        // backstop.
        var result = await processRunner.RunExecutableAsync(
            "git", ["--no-pager", "pull", "--ff-only"], projectRoot, timeout: NetworkTimeout);
        return Merged(result);
    }

    public async Task<GitSyncResult> SyncRebaseAsync(string projectRoot)
    {
        // --rebase explicitly, so the repo's own pull.rebase setting does not matter. Same
        // GIT_TERMINAL_PROMPT=0 + NetworkTimeout reasoning as the ff-only pull above.
        var pull = Merged(await processRunner.RunExecutableAsync(
            "git", ["--no-pager", "pull", "--rebase"], projectRoot, timeout: NetworkTimeout));
        if (!pull.Succeeded)
        {
            if (!await RebaseInProgressAsync(projectRoot))
                return new GitSyncResult(pull, [], null);

            var conflicts = await RunAsync(projectRoot, "diff", "--name-only", "--diff-filter=U");
            var files = conflicts.StdOut.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var abort = await RunAsync(projectRoot, "rebase", "--abort");
            var output = abort.Succeeded
                ? pull.Output
                : pull.Output + "\n" + Merged(abort).Output; // the tree is mid-rebase: say so, verbatim
            return new GitSyncResult(pull with { Output = output }, files, null);
        }

        return new GitSyncResult(pull, [], await PushAsync(projectRoot));
    }

    private async Task<bool> RebaseInProgressAsync(string projectRoot)
    {
        foreach (var dir in new[] { "rebase-merge", "rebase-apply" })
        {
            var path = await RunAsync(projectRoot, "rev-parse", "--git-path", dir);
            if (path.Succeeded && Directory.Exists(Path.Combine(projectRoot, path.StdOut.Trim())))
                return true;
        }
        return false;
    }

    public async Task<GitCommandResult> FetchAsync(string projectRoot)
    {
        // --quiet: without it git reports on stderr every ref it considered even when nothing
        // changed, and the card would render a wall of text for a no-op fetch. Same
        // GIT_TERMINAL_PROMPT=0 + NetworkTimeout reasoning as the pull above.
        var result = await processRunner.RunExecutableAsync(
            "git", ["--no-pager", "fetch", "--quiet"], projectRoot, timeout: NetworkTimeout);
        return Merged(result);
    }

    public async Task<GitBranches> ListBranchesAsync(string projectRoot)
    {
        var head = await RunAsync(projectRoot, "rev-parse", "--abbrev-ref", "HEAD");
        var current = head.Succeeded ? head.StdOut.Trim() : null;

        // for-each-ref, not `branch -a`: no pager, no "* " current-branch marker to strip, no
        // column layout, and the full refname tells a local branch from a remote-tracking one
        // without having to guess at the short form.
        var refs = await RunAsync(projectRoot, "for-each-ref", "--format=%(refname)", "refs/heads", "refs/remotes");
        if (!refs.Succeeded)
            return new GitBranches(current, []); // no list means the checkout whitelist refuses everything

        var lines = refs.StdOut.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var locals = new List<string>();
        var remoteOnly = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Locals first, so a name that exists both here and on a remote is claimed as local and the
        // remote pass below collapses into it rather than adding a second entry for the same branch.
        foreach (var refName in lines)
        {
            if (!refName.StartsWith("refs/heads/", StringComparison.Ordinal))
                continue;
            var name = refName["refs/heads/".Length..];
            if (name.Length > 0 && seen.Add(name))
                locals.Add(name);
        }

        foreach (var refName in lines)
        {
            if (!refName.StartsWith("refs/remotes/", StringComparison.Ordinal))
                continue;

            // "refs/remotes/origin/werk-fase-2" -> remote "origin", branch "werk-fase-2".
            var shortName = refName["refs/remotes/".Length..];
            var slash = shortName.IndexOf('/');
            if (slash < 0)
                continue;
            var name = shortName[(slash + 1)..];

            // <remote>/HEAD is the remote's default-branch symref, not a branch to switch to.
            if (name.Length == 0 || name == "HEAD")
                continue;

            // Two remotes carrying the same name collapse into one entry too — `git switch` refuses
            // that one as ambiguous, and its refusal is exactly the message to show (IGitClient).
            if (seen.Add(name))
                remoteOnly.Add(name);
        }

        var branches = locals.Select(n => new GitBranch(n, RemoteOnly: false))
            .Concat(remoteOnly.Select(n => new GitBranch(n, RemoteOnly: true)))
            .ToList();
        return new GitBranches(current, branches);
    }

    public async Task<GitCommandResult> SwitchAsync(string projectRoot, string branch)
    {
        // No `--` separator to add and none needed: `git switch` takes exactly one branch name, and
        // the endpoint has already whitelisted it against ListBranchesAsync, so no caller-supplied
        // text ever reaches this argument. Local work only, hence GitTimeout and not NetworkTimeout:
        // a remote-only name is created from a ref this copy already has.
        var result = await RunAsync(projectRoot, "switch", branch);
        return Merged(result);
    }

    /// <summary>Parses `git status --porcelain` lines ("XY path" or "XY old -&gt; new" for
    /// renames) into plain relative paths — good enough for a "which spec file changed" default
    /// message, not a full porcelain-v2 parser.</summary>
    private static IReadOnlyList<string> ParseChangedFiles(string porcelainOutput)
    {
        var files = new List<string>();
        foreach (var raw in porcelainOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 4)
                continue;
            var path = line[3..].Trim();
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                path = path[(arrow + 4)..];
            files.Add(path.Trim('"'));
        }
        return files;
    }

    /// <summary>git splits its own reporting across both streams — "Already up to date." on stdout,
    /// "fatal: Not possible to fast-forward" on stderr — and the card shows whichever happened.</summary>
    private static GitCommandResult Merged(ProcessResult result)
    {
        var output = string.Join('\n',
            new[] { result.StdOut.Trim(), result.StdErr.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
        return new GitCommandResult(result.ExitCode, output, result.TimedOut);
    }

    // --no-pager: log (unlike status/rev-parse/add/commit) would otherwise invoke a pager.
    private Task<ProcessResult> RunAsync(string projectRoot, params string[] args) =>
        processRunner.RunExecutableAsync("git", ["--no-pager", .. args], projectRoot, timeout: GitTimeout);
}
