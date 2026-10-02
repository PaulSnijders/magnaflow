using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>Write #10, "switch branch" (docs/prompts/0014). Mirrors GitPullApiIntegrationTests: the
/// endpoints' guards and result passthrough go through the existing FakeGitClient seam, while the
/// real `git switch` against a live remote (does a remote-only name really produce a local branch
/// with an upstream?) is left to live verification, same note as write #8.</summary>
public class GitBranchApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;

    public GitBranchApiIntegrationTests()
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

    private static FakeGitClient CleanRepoOn(string current, params GitBranch[] branches) => new()
    {
        InfoToReturn = new GitInfo(true, current, Dirty: false, [], []),
        BranchesToReturn = new GitBranches(current, branches),
    };

    private static Task<HttpResponseMessage> Checkout(HttpClient client, string project, string branch) =>
        client.PostAsJsonAsync($"/api/projects/{project}/git/checkout", new GitCheckoutRequest(branch));

    [Fact]
    public async Task Branches_lists_locals_and_remote_only_names_without_fetching()
    {
        _project.InitGit();
        var git = CleanRepoOn("werk-fase-1",
            new GitBranch("main", RemoteOnly: false),
            new GitBranch("werk-fase-1", RemoteOnly: false),
            new GitBranch("werk-fase-2", RemoteOnly: true));

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<GitBranchesResponse>("/api/projects/proj/git/branches");

        Assert.Equal("werk-fase-1", result!.Current);
        Assert.Equal(["main", "werk-fase-1", "werk-fase-2"], result.Branches.Select(b => b.Name));
        Assert.True(result.Branches.Single(b => b.Name == "werk-fase-2").RemoteOnly);
        Assert.Null(result.FetchError);
        Assert.Empty(git.FetchCalls); // the network call is the human's explicit choice, never a page load
    }

    [Fact]
    public async Task Branches_with_fetch_true_fetches_first()
    {
        _project.InitGit();
        var git = CleanRepoOn("main", new GitBranch("main", false));

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<GitBranchesResponse>("/api/projects/proj/git/branches?fetch=true");

        Assert.Null(result!.FetchError);
        Assert.Equal(_project.Root, Assert.Single(git.FetchCalls));
    }

    [Fact]
    public async Task Branches_still_returns_the_local_list_when_the_fetch_fails()
    {
        _project.InitGit();
        var git = CleanRepoOn("main", new GitBranch("main", false));
        git.FetchResult = new GitCommandResult(128, "fatal: unable to access 'https://...': Could not resolve host", false);

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/proj/git/branches?fetch=true");
        response.EnsureSuccessStatusCode(); // an offline worker is information, not a failed request
        var result = await response.Content.ReadFromJsonAsync<GitBranchesResponse>();

        Assert.Contains("Could not resolve host", result!.FetchError);
        Assert.Equal("main", Assert.Single(result.Branches).Name);
    }

    [Fact]
    public async Task Branches_on_unknown_project_is_404()
    {
        using var factory = FactoryWith(new FakeGitClient());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/nope/git/branches");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_on_unknown_project_is_404()
    {
        using var factory = FactoryWith(new FakeGitClient());
        using var client = factory.CreateClient();

        var response = await Checkout(client, "nope", "main");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_refused_with_400_for_a_branch_outside_the_list()
    {
        _project.InitGit();
        var git = CleanRepoOn("main", new GitBranch("main", false));

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        // The list is the whitelist — anything else, injection attempt or plain typo, never reaches
        // a git argument.
        var response = await Checkout(client, "proj", "main; rm -rf /");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(git.SwitchCalls);
    }

    [Fact]
    public async Task Checkout_refused_with_409_while_a_command_is_running()
    {
        _project.WriteCmd("0001-busy", status: "running");
        _project.InitGit();
        var git = CleanRepoOn("main", new GitBranch("main", false), new GitBranch("werk-fase-2", true));

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await Checkout(client, "proj", "werk-fase-2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(git.SwitchCalls); // never swapped the tree under the live run
    }

    [Fact]
    public async Task Checkout_refused_with_409_on_a_dirty_tree()
    {
        _project.InitGit();
        var git = CleanRepoOn("main", new GitBranch("main", false), new GitBranch("werk-fase-2", true));
        git.InfoToReturn = new GitInfo(true, "main", Dirty: true, [], []);

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await Checkout(client, "proj", "werk-fase-2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Commit all", body); // message points at the commit-all button, as pull's does
        Assert.Empty(git.SwitchCalls);
    }

    [Fact]
    public async Task Checkout_on_a_clean_tree_switches_and_passes_the_result_through()
    {
        _project.InitGit();
        var git = CleanRepoOn("main", new GitBranch("main", false), new GitBranch("werk-fase-2", true));
        git.SwitchResult = new GitCommandResult(0, "branch 'werk-fase-2' set up to track 'origin/werk-fase-2'.", false);

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await Checkout(client, "proj", "werk-fase-2");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GitCheckoutResponse>();

        Assert.True(result!.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("set up to track", result.Output);
        Assert.Equal("werk-fase-2", result.Branch);
        Assert.Equal((_project.Root, "werk-fase-2"), Assert.Single(git.SwitchCalls));
    }

    [Fact]
    public async Task Checkout_that_git_refuses_returns_the_output_not_an_error()
    {
        _project.InitGit();
        var git = CleanRepoOn("main", new GitBranch("main", false), new GitBranch("werk-fase-2", true));
        git.SwitchResult = new GitCommandResult(128, "fatal: 'werk-fase-2' matched multiple (2) remote tracking branches", false);

        using var factory = FactoryWith(git);
        using var client = factory.CreateClient();

        var response = await Checkout(client, "proj", "werk-fase-2");
        response.EnsureSuccessStatusCode(); // git's own refusal is information, delivered as 200
        var result = await response.Content.ReadFromJsonAsync<GitCheckoutResponse>();

        Assert.False(result!.Success);
        Assert.Equal(128, result.ExitCode);
        Assert.Contains("multiple", result.Output);
    }
}
