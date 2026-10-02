using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>Write #7, "remove project" (ontwerp-v0.5.md item 4): DELETE /api/projects/{name}
/// unregisters (never deletes) — a text edit removing one entry from magnaflow.yml, plus the
/// in-memory drop. Guards: 409 while a command is running.</summary>
public class RemoveProjectApiIntegrationTests : IDisposable
{
    private readonly TempProject _alpha = new();
    private readonly TempProject _beta = new();
    private readonly string _configPath;

    public RemoveProjectApiIntegrationTests()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        CockpitFactory.WriteConfig(_configPath, ("alpha", _alpha.Root), ("beta", _beta.Root));
    }

    public void Dispose()
    {
        _alpha.Dispose();
        _beta.Dispose();
        try { File.Delete(_configPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Delete_unregisters_the_project_edits_the_yml_and_leaves_the_working_copy_on_disk()
    {
        _alpha.InitGit();
        _beta.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/projects/alpha");
        response.EnsureSuccessStatusCode();

        // No longer served, sibling still is.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/alpha")).StatusCode);
        var remaining = await client.GetFromJsonAsync<List<ProjectSummaryDto>>("/api/projects");
        var kept = Assert.Single(remaining!);
        Assert.Equal("beta", kept.Name);

        // magnaflow.yml edited: alpha gone, beta survives; no lingering .bak.
        var yml = await File.ReadAllTextAsync(_configPath);
        Assert.DoesNotContain("name: alpha", yml);
        Assert.Contains("name: beta", yml);
        Assert.False(File.Exists(_configPath + ".bak"));

        // The working copy itself is untouched — unregister, not delete.
        Assert.True(Directory.Exists(_alpha.Root));
        Assert.True(Directory.Exists(Path.Combine(_alpha.Root, ".git")));
    }

    [Fact]
    public async Task Delete_is_refused_with_409_while_a_command_is_running()
    {
        _alpha.WriteCmd("0001-busy", status: "running");
        _alpha.InitGit();
        _beta.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/projects/alpha");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // Still registered and still in the file.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/projects/alpha")).StatusCode);
        Assert.Contains("name: alpha", await File.ReadAllTextAsync(_configPath));
    }

    [Fact]
    public async Task Delete_on_unknown_project_is_404()
    {
        _alpha.InitGit();
        _beta.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/projects/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
