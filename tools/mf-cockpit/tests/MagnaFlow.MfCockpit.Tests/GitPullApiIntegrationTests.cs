using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>Write #8, "git pull" (ontwerp-v0.5.md item 7). The endpoint's guards and result
/// passthrough are exercised through the existing FakeGitClient seam; the real
/// `git pull --ff-only` invocation (which needs a live upstream) is left to live verification per
/// the design's KISS note.</summary>
public class GitPullApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;

    public GitPullApiIntegrationTests()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        CockpitFactory.WriteConfig(_configPath, ("proj", _project.Root));
    }

    public void Dispose()
    {
        _project.Dispose();
        try { File.Delete(_configPath); } catch { /* best effort */ }
    }

    private CockpitFactory FactoryWith(FakeGitClient git) =>
        new(_configPath, s => s.AddSingleton<IGitClient>(git));

    [Fact]
    public async Task Pull_refused_with_409_while_a_command_is_running()
    {
        _project.WriteCmd("0001-busy", status: "running");
        _project.InitGit();
        var git = new FakeGitClient();

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/git/pull", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(git.PullCalls); // never reached the pull itself
    }

    [Fact]
    public async Task Pull_refused_with_409_on_a_dirty_tree()
    {
        _project.InitGit();
        var git = new FakeGitClient { InfoToReturn = new GitInfo(true, "main", Dirty: true, [], []) };

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/git/pull", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Commit all", body); // message points at the commit-all button
        Assert.Empty(git.PullCalls);
    }

    [Fact]
    public async Task Pull_on_a_clean_tree_runs_and_passes_the_result_through()
    {
        _project.InitGit();
        var git = new FakeGitClient
        {
            InfoToReturn = new GitInfo(true, "main", Dirty: false, [], []),
            PullResult = new GitCommandResult(0, "Already up to date.", false),
        };

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/git/pull", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GitPullResponse>();

        Assert.True(result!.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("up to date", result.Output);
        Assert.Equal(_project.Root, Assert.Single(git.PullCalls));
    }

    [Fact]
    public async Task Pull_that_cannot_fast_forward_returns_the_output_not_an_error()
    {
        _project.InitGit();
        var git = new FakeGitClient
        {
            InfoToReturn = new GitInfo(true, "main", Dirty: false, [], []),
            PullResult = new GitCommandResult(128, "fatal: Not possible to fast-forward, aborting.", false),
        };

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/git/pull", content: null);
        response.EnsureSuccessStatusCode(); // an ff-refusal is information, delivered as 200
        var result = await response.Content.ReadFromJsonAsync<GitPullResponse>();

        Assert.False(result!.Success);
        Assert.Equal(128, result.ExitCode);
        Assert.Contains("fast-forward", result.Output);
    }

    [Fact]
    public async Task Pull_on_unknown_project_is_404()
    {
        using var factory = FactoryWith(new FakeGitClient());
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/nope/git/pull", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
