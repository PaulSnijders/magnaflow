using System.Net;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// Every static file is served with Cache-Control: no-cache, so after an install the browser
/// revalidates (ETag) instead of mixing an old app.js with a new style.css (docs/prompts/0024).
/// </summary>
public class StaticFilesCacheTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;

    public StaticFilesCacheTests()
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

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/specs.html")]
    [InlineData("/assets/app.js")]
    [InlineData("/assets/style.css")]
    public async Task Static_file_is_served_with_no_cache_and_an_etag(string path)
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache, $"{path}: Cache-Control was '{response.Headers.CacheControl}'");
        Assert.NotNull(response.Headers.ETag);
    }
}
