using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// GET/PUT /api/projects/{name}/config — write #5 (ontwerp-v0.3.md "The Config page"). Exercises
/// the real endpoint against a disposable git repo, same posture as the other lane writes in
/// ApiIntegrationTests.
/// </summary>
public class ProjectConfigApiIntegrationTests : IDisposable
{
    private const string InitialConfig = """
        # comment above build
        build:
          command: dotnet build
        test:
          command: dotnet test
        defaults:
          max_attempts: 3
        """;

    private readonly TempProject _project = new();
    private readonly string _configPath;

    public ProjectConfigApiIntegrationTests()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        CockpitFactory.WriteConfig(_configPath, ("proj", _project.Root));
        _project.WriteFile(".magnaflow/config.yml", InitialConfig);
        _project.InitGit();
    }

    public void Dispose()
    {
        _project.Dispose();
        try { File.Delete(_configPath); } catch { /* best effort */ }
    }

    private string ConfigFilePath => Path.Combine(_project.Root, ".magnaflow", "config.yml");

    [Fact]
    public async Task Get_returns_raw_content_hash_and_parsed_summary()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var dto = await client.GetFromJsonAsync<ProjectConfigDto>("/api/projects/proj/config");

        Assert.Equal(InitialConfig, dto!.Content);
        Assert.NotEmpty(dto.Hash);
        Assert.Equal(["dotnet build"], dto.Summary.BuildCommands);
        Assert.Equal(3, dto.Summary.MaxAttempts);
    }

    [Fact]
    public async Task Get_on_unknown_project_is_404()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/does-not-exist/config");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_when_no_config_file_exists_is_404()
    {
        File.Delete(ConfigFilePath);
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/proj/config");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Round_trip_preserves_content_byte_for_byte_outside_the_edited_line_and_commits()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var original = await client.GetFromJsonAsync<ProjectConfigDto>("/api/projects/proj/config");
        var edited = original!.Content.Replace("max_attempts: 3", "max_attempts: 7");

        var response = await client.PutAsJsonAsync("/api/projects/proj/config", new { content = edited, baseHash = original.Hash });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProjectConfigSaveResponse>();

        Assert.Empty(result!.Warnings);
        Assert.NotEqual(original.Hash, result.Hash);

        var onDisk = File.ReadAllText(ConfigFilePath);
        Assert.Equal(edited, onDisk);
        Assert.Contains("# comment above build", onDisk);
        Assert.Contains("command: dotnet build", onDisk);
        Assert.Contains("max_attempts: 7", onDisk);

        Assert.Contains("cockpit: edit config", string.Join('\n', _project.GitLog()));

        var reread = await client.GetFromJsonAsync<ProjectConfigDto>("/api/projects/proj/config");
        Assert.Equal(result.Hash, reread!.Hash);
        Assert.Equal(7, reread.Summary.MaxAttempts);
    }

    [Fact]
    public async Task Stale_hash_is_409_and_does_not_touch_the_file_or_git()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/projects/proj/config", new { content = "build:\n  command: x\n", baseHash = "not-the-real-hash" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(InitialConfig, File.ReadAllText(ConfigFilePath));
        Assert.DoesNotContain("cockpit: edit config", string.Join('\n', _project.GitLog()));
    }

    [Fact]
    public async Task Broken_yaml_is_400_with_the_parse_error_and_hash_drift_takes_priority_over_it()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        // Stale hash AND broken YAML at once: hash match is checked first (ontwerp-v0.3.md "Save
        // flow" — "enforces, in order: hash match, YAML parses, size cap"), so this must be 409,
        // not 400.
        var staleAndBroken = await client.PutAsJsonAsync("/api/projects/proj/config", new { content = "build: [not valid", baseHash = "stale" });
        Assert.Equal(HttpStatusCode.Conflict, staleAndBroken.StatusCode);

        var currentHash = (await client.GetFromJsonAsync<ProjectConfigDto>("/api/projects/proj/config"))!.Hash;
        var response = await client.PutAsJsonAsync("/api/projects/proj/config", new { content = "build: [not valid", baseHash = currentHash });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Contains("line", body!["error"]);
        Assert.Equal(InitialConfig, File.ReadAllText(ConfigFilePath));
    }

    [Fact]
    public async Task Unknown_top_level_key_is_a_warning_not_a_rejection_and_still_saves()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var original = await client.GetFromJsonAsync<ProjectConfigDto>("/api/projects/proj/config");
        var edited = original!.Content + "\nfuture_tool:\n  flag: true\n";

        var response = await client.PutAsJsonAsync("/api/projects/proj/config", new { content = edited, baseHash = original.Hash });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProjectConfigSaveResponse>();
        Assert.Single(result!.Warnings);
        Assert.Contains("future_tool", result.Warnings[0]);
        Assert.Contains("future_tool", File.ReadAllText(ConfigFilePath));
    }

    [Fact]
    public async Task Oversized_content_is_400_and_does_not_write()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var original = await client.GetFromJsonAsync<ProjectConfigDto>("/api/projects/proj/config");
        var huge = "build:\n  command: dotnet build\n# " + new string('x', 300 * 1024);

        var response = await client.PutAsJsonAsync("/api/projects/proj/config", new { content = huge, baseHash = original!.Hash });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InitialConfig, File.ReadAllText(ConfigFilePath));
    }

    [Fact]
    public async Task Put_on_unknown_project_is_404()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/projects/does-not-exist/config", new { content = "build:\n  command: x\n", baseHash = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
