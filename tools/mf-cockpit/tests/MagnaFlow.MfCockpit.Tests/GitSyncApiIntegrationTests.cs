using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>Sync (docs/prompts/0025): `git pull --rebase` then `git push`, for a diverged copy. The
/// guards go through the FakeGitClient seam like the pull's; the rebase itself runs against real
/// git (a bare remote plus a second "desktop" clone that pushes in between), because what a
/// stopped rebase leaves behind — and whether the abort really restores the tree — is exactly what
/// a fake would only assume.</summary>
public class GitSyncApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N"));

    public GitSyncApiIntegrationTests()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        CockpitFactory.WriteConfig(_configPath, ("proj", _project.Root));
    }

    public void Dispose()
    {
        _project.Dispose();
        try { File.Delete(_configPath); } catch { /* best effort */ }
        try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ }
    }

    private CockpitFactory FactoryWith(FakeGitClient git) =>
        new(_configPath, s => s.AddSingleton<IGitClient>(git));

    private static string Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        return stdout.Trim();
    }

    private static void Commit(string repo, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(repo, file), content);
        Git(repo, "add", "-A");
        Git(repo, "commit", "-q", "-m", message);
    }

    /// <summary>The project as a clone of a bare remote, plus a desktop clone that has pushed
    /// <paramref name="desktopContent"/> to <paramref name="desktopFile"/> while the project
    /// committed <paramref name="localContent"/> to <paramref name="localFile"/>. Fetched, so the
    /// project knows it is diverged.</summary>
    private void Diverge(string localFile, string localContent, string desktopFile, string desktopContent)
    {
        _project.WriteFile("shared.md", "line one\n");
        _project.InitGit();
        var remote = Path.Combine(_scratch, "remote.git");
        var desktop = Path.Combine(_scratch, "desktop");
        Directory.CreateDirectory(_scratch);
        Git(_scratch, "init", "-q", "--bare", "-b", "main", remote);
        Git(_project.Root, "remote", "add", "origin", remote);
        Git(_project.Root, "push", "-q", "-u", "origin", "main");
        Git(_scratch, "clone", "-q", remote, desktop);
        Git(desktop, "config", "user.email", "test@example.com");
        Git(desktop, "config", "user.name", "test");
        Commit(desktop, desktopFile, desktopContent, "desktop commit");
        Git(desktop, "push", "-q");
        Commit(_project.Root, localFile, localContent, "local commit");
        Git(_project.Root, "fetch", "-q");
    }

    [Fact]
    public async Task Sync_refused_with_409_while_a_command_is_running()
    {
        _project.WriteCmd("0001-busy", status: "running");
        _project.InitGit();
        var git = new FakeGitClient();

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/git/sync", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(git.SyncCalls);
    }

    [Fact]
    public async Task Sync_refused_with_409_on_a_dirty_tree()
    {
        _project.InitGit();
        var git = new FakeGitClient { InfoToReturn = new GitInfo(true, "main", Dirty: true, [], [], Ahead: 1, Behind: 1) };

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/git/sync", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Commit all", await response.Content.ReadAsStringAsync());
        Assert.Empty(git.SyncCalls);
    }

    [Fact]
    public async Task Git_info_reports_ahead_and_behind_for_a_diverged_copy()
    {
        Diverge("local.md", "mine\n", "desktop.md", "theirs\n");

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var info = await client.GetFromJsonAsync<ProjectGitInfoDto>("/api/projects/proj/git");

        Assert.Equal(1, info!.Ahead);
        Assert.Equal(1, info.Behind);
    }

    [Fact]
    public async Task Sync_rebases_the_local_commit_onto_the_remote_and_pushes()
    {
        Diverge("local.md", "mine\n", "desktop.md", "theirs\n");

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/git/sync", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GitSyncResponse>();

        Assert.True(result!.Success);
        Assert.Empty(result.Conflicts);
        Assert.True(result.Push!.Success);
        Assert.Equal("0\t0", Git(_project.Root, "rev-list", "--left-right", "--count", "HEAD...@{u}"));
        Assert.Equal("", Git(_project.Root, "rev-list", "--merges", "HEAD")); // linear: rebased, not merged
    }

    [Fact]
    public async Task Sync_conflict_aborts_leaves_the_tree_unchanged_and_returns_the_output()
    {
        Diverge("shared.md", "my line\n", "shared.md", "their line\n");
        var before = Git(_project.Root, "rev-parse", "HEAD");

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/git/sync", content: null);
        response.EnsureSuccessStatusCode(); // a conflict is information, delivered as 200
        var result = await response.Content.ReadFromJsonAsync<GitSyncResponse>();

        Assert.False(result!.Success);
        Assert.Equal(["shared.md"], result.Conflicts);
        Assert.Contains("CONFLICT", result.Output);
        Assert.Null(result.Push);
        Assert.Equal(before, Git(_project.Root, "rev-parse", "HEAD"));
        Assert.Equal("", Git(_project.Root, "status", "--porcelain"));
        Assert.Equal("my line", File.ReadAllText(Path.Combine(_project.Root, "shared.md")).Trim());
        Assert.False(Directory.Exists(Path.Combine(_project.Root, ".git", "rebase-merge")));
    }
}
