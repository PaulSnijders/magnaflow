using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>GET/PUT /api/projects/{name}/scratchpad (ontwerp-v0.5.md item 8): hash-based optimistic
/// concurrency (409), the 256 KB cap, and the store landing next to the loaded magnaflow.yml —
/// never inside the project working copy.</summary>
public class ScratchpadApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;

    public ScratchpadApiIntegrationTests()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        CockpitFactory.WriteConfig(_configPath, ("proj", _project.Root));
    }

    public void Dispose()
    {
        _project.Dispose();
        try { File.Delete(_configPath); } catch { /* best effort */ }
        try { Directory.Delete(Path.Combine(Path.GetDirectoryName(_configPath)!, "scratchpad"), recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Get_of_a_fresh_scratchpad_is_empty()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var dto = await client.GetFromJsonAsync<ScratchpadDto>("/api/projects/proj/scratchpad");

        Assert.Equal("", dto!.Content);
        Assert.Null(dto.SavedAt);
    }

    [Fact]
    public async Task Put_then_get_round_trips_and_stores_outside_the_working_copy()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var initial = await client.GetFromJsonAsync<ScratchpadDto>("/api/projects/proj/scratchpad");
        var put = await client.PutAsJsonAsync("/api/projects/proj/scratchpad", new { content = "remember this", baseHash = initial!.Hash });
        put.EnsureSuccessStatusCode();

        var reloaded = await client.GetFromJsonAsync<ScratchpadDto>("/api/projects/proj/scratchpad");
        Assert.Equal("remember this", reloaded!.Content);

        // The note is next to magnaflow.yml, NOT under the project tree (would dirty the worker's tree).
        var scratchDir = Path.Combine(Path.GetDirectoryName(_configPath)!, "scratchpad");
        Assert.True(File.Exists(Path.Combine(scratchDir, "proj.md")));
        Assert.False(Directory.Exists(Path.Combine(_project.Root, "scratchpad")));
    }

    [Fact]
    public async Task Put_with_a_stale_base_hash_is_409()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var initial = await client.GetFromJsonAsync<ScratchpadDto>("/api/projects/proj/scratchpad");
        var first = await client.PutAsJsonAsync("/api/projects/proj/scratchpad", new { content = "first", baseHash = initial!.Hash });
        first.EnsureSuccessStatusCode();

        // Still holding the original (empty) hash — must be told to reload.
        var stale = await client.PutAsJsonAsync("/api/projects/proj/scratchpad", new { content = "second", baseHash = initial.Hash });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = await client.GetFromJsonAsync<ScratchpadDto>("/api/projects/proj/scratchpad");
        Assert.Equal("first", current!.Content);
    }

    [Fact]
    public async Task Put_over_the_cap_is_400()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var initial = await client.GetFromJsonAsync<ScratchpadDto>("/api/projects/proj/scratchpad");
        var tooBig = new string('x', ScratchpadStore.MaxContentBytes + 1);
        var response = await client.PutAsJsonAsync("/api/projects/proj/scratchpad", new { content = tooBig, baseHash = initial!.Hash });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Scratchpad_on_unknown_project_is_404()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/nope/scratchpad");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
