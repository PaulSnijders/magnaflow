using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>GET /api/browse (docs/specs/cockpit/index.md "Browse") against the real app.</summary>
public class BrowseApiIntegrationTests : IDisposable
{
    private readonly string _configPath;
    private readonly string _tree;

    public BrowseApiIntegrationTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests");
        _configPath = Path.Combine(baseDir, Guid.NewGuid().ToString("N") + ".yml");
        _tree = Path.Combine(baseDir, "browse-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tree, "alpha", ".git"));
        Directory.CreateDirectory(Path.Combine(_tree, "Beta"));
        File.WriteAllText(Path.Combine(_tree, "file.txt"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tree, recursive: true); } catch { /* best effort */ }
        try { File.Delete(_configPath); } catch { /* best effort */ }
    }

    private CockpitFactory Factory(string? root = null)
    {
        CockpitFactory.WriteConfigWithNewProject(_configPath, new CockpitFactory.NewProjectSpec(root, null, null));
        return new CockpitFactory(_configPath);
    }

    [Fact]
    public async Task Lists_the_given_folder()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var listing = await client.GetFromJsonAsync<BrowseListingDto>("/api/browse?path=" + Uri.EscapeDataString(_tree));

        Assert.Equal(_tree, listing!.Path);
        Assert.Equal(["alpha", "Beta"], listing.Dirs.Select(d => d.Name));
        Assert.True(listing.Dirs[0].Git);
        Assert.False(listing.Dirs[1].Git);
    }

    [Fact]
    public async Task A_relative_path_is_400_with_an_error()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/browse?path=relative");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task A_missing_folder_is_404()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/browse?path=" + Uri.EscapeDataString(Path.Combine(_tree, "missing")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_path_it_lists_the_configured_root()
    {
        using var factory = Factory(root: _tree);
        using var client = factory.CreateClient();

        var listing = await client.GetFromJsonAsync<BrowseListingDto>("/api/browse");

        Assert.Equal(Path.GetFullPath(_tree), listing!.Path);
    }

    [Fact]
    public async Task Without_path_an_existing_hint_wins()
    {
        using var factory = Factory(root: _tree);
        using var client = factory.CreateClient();

        var listing = await client.GetFromJsonAsync<BrowseListingDto>("/api/browse?hint=alpha");

        Assert.Equal(Path.Combine(Path.GetFullPath(_tree), "alpha"), listing!.Path);
    }
}
