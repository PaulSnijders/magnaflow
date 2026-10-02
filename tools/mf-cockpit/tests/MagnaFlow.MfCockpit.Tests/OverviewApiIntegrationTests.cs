using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// GET /api/overview — the one request the shared chrome makes per page load (docs/prompts/0010),
/// plus the server-side memo in front of the two endpoints that spawn processes.
/// </summary>
public class OverviewApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly TempProject _other = new();
    private readonly string _configPath;

    public OverviewApiIntegrationTests()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        CockpitFactory.WriteConfig(_configPath, ("proj", _project.Root), ("other", _other.Root));
    }

    public void Dispose()
    {
        _project.Dispose();
        _other.Dispose();
        try { File.Delete(_configPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Overview_with_a_project_returns_chrome_and_that_project_summary()
    {
        _project.WriteCmd("0001-hello", status: "running", title: "Say hello");
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var overview = await client.GetFromJsonAsync<OverviewDto>("/api/overview?project=proj");

        Assert.Equal(["proj", "other"], overview!.Projects);
        Assert.NotNull(overview.Project);
        Assert.Equal("proj", overview.Project!.Name);
        Assert.Equal(1, overview.Project.Counts.Running);
        Assert.Equal("0001-hello", overview.Project.Running!.Id);
        Assert.True(overview.ChatEnabled); // default when the config declares no chat block
    }

    [Fact]
    public async Task Overview_without_a_project_still_lists_the_projects()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var overview = await client.GetFromJsonAsync<OverviewDto>("/api/overview");

        Assert.Equal(["proj", "other"], overview!.Projects);
        Assert.Null(overview.Project);
    }

    [Fact]
    public async Task Overview_for_an_unknown_project_is_200_with_a_null_project_not_404()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/overview?project=does-not-exist");

        // The nav and the switcher must still render on a page opened with a stale name.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var overview = await response.Content.ReadFromJsonAsync<OverviewDto>();
        Assert.Null(overview!.Project);
        Assert.Equal(["proj", "other"], overview.Projects);
    }

    [Fact]
    public async Task Run_status_is_cached_per_project_and_cleared_by_a_run_action()
    {
        _project.WriteFile(".magnaflow/config.yml", "run:\n  services:\n    - name: web\n      command: web.exe\n");
        var runClient = new FakeRunClient { StatusOutput = """[{"name":"web","running":false}]""" };
        using var factory = new CockpitFactory(_configPath, s => s.AddSingleton<IRunClient>(runClient));
        using var client = factory.CreateClient();

        await client.GetAsync("/api/projects/proj/run");
        await client.GetAsync("/api/projects/proj/run");
        Assert.Equal(1, runClient.StatusCalls); // second read served from the memo — no second spawn

        await client.PostAsync("/api/projects/proj/run/web/start", content: null);
        await client.GetAsync("/api/projects/proj/run");

        // The status read a button triggers after itself must never show the state from before it.
        Assert.Equal(2, runClient.StatusCalls);
    }

    [Fact]
    public async Task Git_info_is_cached_per_project_and_cleared_by_commit_all()
    {
        _project.InitGit();
        var git = new FakeGitClient();
        using var factory = new CockpitFactory(_configPath, s => s.AddSingleton<IGitClient>(git));
        using var client = factory.CreateClient();

        await client.GetAsync("/api/projects/proj/git");
        await client.GetAsync("/api/projects/proj/git");
        Assert.Equal(1, git.InfoCalls);

        await client.PostAsJsonAsync("/api/projects/proj/git/commit-all", new { message = "wip" });
        await client.GetAsync("/api/projects/proj/git");

        Assert.Equal(2, git.InfoCalls);
    }

    [Fact]
    public async Task The_memo_is_per_project_so_one_project_never_serves_another()
    {
        _project.InitGit();
        _other.InitGit();
        var git = new FakeGitClient();
        using var factory = new CockpitFactory(_configPath, s => s.AddSingleton<IGitClient>(git));
        using var client = factory.CreateClient();

        await client.GetAsync("/api/projects/proj/git");
        await client.GetAsync("/api/projects/other/git");

        Assert.Equal(2, git.InfoCalls);
    }
}
