using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// Write #6 end-to-end (ontwerp-v0.4.md "Creating a project"), against the real app hosted
/// in-process (WebApplicationFactory) — the full mode:new scaffold pipeline (copy template, command
/// template incl. failure/timeout) and mode:existing registration, each exercising the real
/// filesystem, a real git spawn, and the real magnaflow.yml append.
/// </summary>
public class NewProjectApiIntegrationTests : IDisposable
{
    private readonly string _configPath;
    private readonly List<string> _scratchDirs = [];

    public NewProjectApiIntegrationTests()
    {
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
    }

    public void Dispose()
    {
        foreach (var dir in _scratchDirs)
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        try { File.Delete(_configPath); } catch { /* best effort */ }
    }

    private string ScratchDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _scratchDirs.Add(dir);
        return dir;
    }

    private static string StubPath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public async Task Mode_new_with_a_copy_template_scaffolds_registers_and_seeds_a_draft()
    {
        var root = ScratchDir("root");
        var templateSource = ScratchDir("tmpl");
        File.WriteAllText(Path.Combine(templateSource, "README.md"), "hello template");
        var specKitSource = ScratchDir("speckit");
        File.WriteAllText(Path.Combine(specKitSource, "0001-adopt-spec-system.md"), "# adopt");

        CockpitFactory.WriteConfigWithNewProject(_configPath, new CockpitFactory.NewProjectSpec(
            root, specKitSource, [new CockpitFactory.NewProjectTemplateSpec("basic", "copy", Source: templateSource)]));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "new", name = "My New Project", template = "basic" });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateProjectResponse>();

        var targetPath = Path.Combine(root, "My-New-Project");
        Assert.Equal("My New Project", result!.Name);
        Assert.Equal(targetPath, result.Path);

        // template copied
        Assert.True(File.Exists(Path.Combine(targetPath, "README.md")));
        // spec kit copied to docs/spec-kit/
        Assert.True(File.Exists(Path.Combine(targetPath, "docs", "spec-kit", "0001-adopt-spec-system.md")));
        // hygiene files
        var gitignore = await File.ReadAllTextAsync(Path.Combine(targetPath, ".gitignore"));
        Assert.Contains(".magnaflow/*", gitignore);
        Assert.Contains("!.magnaflow/config.yml", gitignore);
        Assert.True(gitignore.IndexOf(".magnaflow/*", StringComparison.Ordinal)
                    < gitignore.IndexOf("!.magnaflow/config.yml", StringComparison.Ordinal),
            "the negation must come after the wildcard or git ignores config.yml too");
        Assert.True(File.Exists(Path.Combine(targetPath, ".magnaflow", "config.yml")));

        // initial commit + seeded draft's own commit
        var log = TempProject.GitLogAt(targetPath);
        Assert.Contains(log, l => l.Contains("cockpit: create project My New Project"));
        Assert.Contains(log, l => l.Contains("cockpit: create draft 0001-adopt-spec-system"));

        // seeded draft is a draft in the lane
        Assert.Equal("0001-adopt-spec-system", result.SeededDraftId);
        var draftPath = Path.Combine(targetPath, "docs", "prompts", "0001-cmd-adopt-spec-system.md");
        Assert.True(File.Exists(draftPath));
        Assert.Contains("status: draft", await File.ReadAllTextAsync(draftPath));

        // registered in magnaflow.yml, no lingering .bak
        var configContent = await File.ReadAllTextAsync(_configPath);
        Assert.Contains("- name: My New Project", configContent);
        Assert.Contains($"path: {targetPath.Replace('\\', '/')}", configContent);
        Assert.False(File.Exists(_configPath + ".bak"));

        // served without a restart
        var summaryResponse = await client.GetAsync($"/api/projects/{Uri.EscapeDataString("My New Project")}");
        summaryResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Mode_new_with_a_command_template_runs_the_stub_with_placeholders_substituted()
    {
        if (!OperatingSystem.IsWindows()) return; // fixtures are .cmd stubs
        var root = ScratchDir("root");
        CockpitFactory.WriteConfigWithNewProject(_configPath, new CockpitFactory.NewProjectSpec(root, null,
            [new CockpitFactory.NewProjectTemplateSpec("gen", "command",
                Command: StubPath("template-stub.cmd"), Args: ["--target", "{target}", "--name", "{name}"], TimeoutSeconds: 30)]));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "new", name = "gen-demo", template = "gen" });
        response.EnsureSuccessStatusCode();

        var targetPath = Path.Combine(root, "gen-demo");
        Assert.True(File.Exists(Path.Combine(targetPath, "scaffold-marker.txt")), "the stub's own side effect proves it actually ran");
    }

    [Fact]
    public async Task Mode_new_with_a_failing_command_template_leaves_the_directory_in_place_and_registers_nothing()
    {
        if (!OperatingSystem.IsWindows()) return; // fixtures are .cmd stubs
        var root = ScratchDir("root");
        CockpitFactory.WriteConfigWithNewProject(_configPath, new CockpitFactory.NewProjectSpec(root, null,
            [new CockpitFactory.NewProjectTemplateSpec("gen", "command", Command: StubPath("template-stub-fail.cmd"), TimeoutSeconds: 30)]));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "new", name = "fail-demo", template = "gen" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("exited 3", body);

        var targetPath = Path.Combine(root, "fail-demo");
        Assert.True(Directory.Exists(targetPath), "a failed scaffold leaves the directory in place for diagnosis");

        Assert.DoesNotContain("fail-demo", await File.ReadAllTextAsync(_configPath));
        var notFound = await client.GetAsync("/api/projects/fail-demo");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task Mode_new_with_a_hanging_command_template_is_killed_by_the_configured_timeout()
    {
        if (!OperatingSystem.IsWindows()) return; // fixtures are .cmd stubs
        var root = ScratchDir("root");
        CockpitFactory.WriteConfigWithNewProject(_configPath, new CockpitFactory.NewProjectSpec(root, null,
            [new CockpitFactory.NewProjectTemplateSpec("gen", "command", Command: StubPath("template-stub-hang.cmd"), TimeoutSeconds: 3)]));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "new", name = "hang-demo", template = "gen" });
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("timed out", body);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"took {stopwatch.Elapsed} — timeout did not kill the process promptly");

        var notFound = await client.GetAsync("/api/projects/hang-demo");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task Mode_new_rejects_a_target_that_already_exists()
    {
        var root = ScratchDir("root");
        Directory.CreateDirectory(Path.Combine(root, "taken"));
        CockpitFactory.WriteConfigWithNewProject(_configPath, new CockpitFactory.NewProjectSpec(root, null, null));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "new", name = "taken" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Mode_new_without_root_configured_is_a_bad_request()
    {
        CockpitFactory.WriteConfig(_configPath);
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "new", name = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_mode_is_a_bad_request()
    {
        CockpitFactory.WriteConfig(_configPath);
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "bogus", name = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Mode_existing_registers_a_working_copy_and_warns_about_a_missing_git_directory()
    {
        var existingDir = ScratchDir("existing");
        CockpitFactory.WriteConfig(_configPath);

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "existing", name = "existing-proj", path = existingDir });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateProjectResponse>();

        Assert.Equal("existing-proj", result!.Name);
        Assert.Null(result.SeededDraftId);
        Assert.Contains(result.Warnings, w => w.Contains(".git"));

        var summary = await client.GetAsync("/api/projects/existing-proj");
        summary.EnsureSuccessStatusCode();
        Assert.Contains("- name: existing-proj", await File.ReadAllTextAsync(_configPath));
    }

    [Fact]
    public async Task Mode_existing_with_a_git_directory_present_gets_no_warning()
    {
        using var existing = new TempProject();
        existing.InitGit();
        CockpitFactory.WriteConfig(_configPath);

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "existing", name = "with-git", path = existing.Root });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateProjectResponse>();

        Assert.Empty(result!.Warnings);
    }

    [Fact]
    public async Task Mode_existing_duplicate_name_is_409()
    {
        using var existing = new TempProject();
        CockpitFactory.WriteConfig(_configPath, ("proj", existing.Root));
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();
        var otherDir = ScratchDir("other");

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "existing", name = "proj", path = otherDir });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Mode_existing_duplicate_path_is_409()
    {
        using var existing = new TempProject();
        CockpitFactory.WriteConfig(_configPath, ("proj", existing.Root));
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "existing", name = "different-name", path = existing.Root });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Mode_existing_rejects_a_path_that_does_not_exist()
    {
        CockpitFactory.WriteConfig(_configPath);
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            mode = "existing",
            name = "ghost",
            path = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", "does-not-exist-" + Guid.NewGuid().ToString("N")),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_new_project_info_exposes_template_names_only_never_command_or_args()
    {
        var root = ScratchDir("root");
        CockpitFactory.WriteConfigWithNewProject(_configPath, new CockpitFactory.NewProjectSpec(root, null,
            [new CockpitFactory.NewProjectTemplateSpec("secret-gen", "command", Command: @"C:\secret\tool.exe", Args: ["--token", "abc123"])]));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/new-project");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var info = JsonSerializer.Deserialize<NewProjectInfoDto>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(root, info!.Root);
        Assert.Equal(["secret-gen"], info.Templates);
        Assert.DoesNotContain(@"C:\secret\tool.exe", body);
        Assert.DoesNotContain("abc123", body);
    }

    [Fact]
    public async Task Get_new_project_info_reports_no_root_when_unconfigured()
    {
        CockpitFactory.WriteConfig(_configPath);
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var info = await client.GetFromJsonAsync<NewProjectInfoDto>("/api/new-project");

        Assert.Null(info!.Root);
        Assert.Empty(info.Templates);
    }

    [Theory]
    [InlineData("existing")]
    [InlineData("new")]
    public async Task A_legacy_mf_cockpit_yml_is_refused_with_400_and_the_remove_project_message(string mode)
    {
        // MF_COCKPIT_CONFIG is explicit, so LocatePath never reports it as legacy — swap in the
        // CockpitConfig the legacy-filename fallback would have produced.
        var root = ScratchDir("root");
        var existingDir = ScratchDir("existing");
        var legacyPath = Path.Combine(ScratchDir("legacy"), CockpitConfig.LegacyFileName);
        File.WriteAllText(legacyPath, $"projects: []\nnew_project:\n  root: {root.Replace('\\', '/')}\n");
        var (legacyConfig, _, _) = CockpitConfig.Load(legacyPath, isLegacyFileName: true);
        CockpitFactory.WriteConfig(_configPath);

        using var factory = new CockpitFactory(_configPath, services =>
        {
            services.AddSingleton(legacyConfig!);
            services.AddSingleton(legacyConfig!.NewProject);
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode, name = "legacy-proj", path = existingDir });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error").GetString();
        Assert.Contains("deprecated mf-cockpit.yml filename", error);
        Assert.Contains("before managing projects here", error);
        Assert.False(Directory.Exists(Path.Combine(root, "legacy-proj")), "nothing is scaffolded for a refused config");
        Assert.Equal($"projects: []\nnew_project:\n  root: {root.Replace('\\', '/')}\n", File.ReadAllText(legacyPath));
    }

    [Fact]
    public async Task A_magnaflow_yml_with_flat_root_cockpit_fields_is_refused_with_400()
    {
        var known = ScratchDir("known");
        var existingDir = ScratchDir("existing");
        var original = $"projects:\n  - name: known\n    path: {known.Replace('\\', '/')}\n";
        File.WriteAllText(_configPath, original);

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "existing", name = "existing-proj", path = existingDir });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("under a top-level 'cockpit:' section", body.GetProperty("error").GetString());
        Assert.Equal(original, await File.ReadAllTextAsync(_configPath));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/existing-proj")).StatusCode);
    }

    [Fact]
    public async Task A_project_added_at_runtime_gets_live_lane_events_without_a_restart()
    {
        using var added = new TempProject();
        Directory.CreateDirectory(added.PromptsDir); // a watcher only covers directories that exist
        CockpitFactory.WriteConfig(_configPath);

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects", new { mode = "existing", name = "added", path = added.Root });
        response.EnsureSuccessStatusCode();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var events = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await using var stream = await events.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        await Task.Delay(300, cts.Token);
        added.WriteCmd("0001-hello", status: "ready");

        string? line;
        var sawLaneEvent = false;
        while (!sawLaneEvent && (line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            if (!line.StartsWith("data: "))
                continue;
            using var doc = JsonDocument.Parse(line["data: ".Length..]);
            sawLaneEvent = doc.RootElement.GetProperty("project").GetString() == "added"
                && doc.RootElement.GetProperty("kind").GetString() == "lane";
        }

        Assert.True(sawLaneEvent);
    }
}
