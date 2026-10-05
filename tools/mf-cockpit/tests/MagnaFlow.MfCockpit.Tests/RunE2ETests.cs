using System.Diagnostics;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// Run card end-to-end against a stub mf-run executable — the same swap-the-command mechanism
/// ChatE2ETests uses for chat.command (ontwerp-v0.3.md's own requirement: "same swap mechanism as
/// chat.command's stub"). Exercises the actual process spawn/timeout path that
/// RunApiIntegrationTests' FakeRunClient deliberately bypasses.
/// </summary>
public class RunE2ETests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;

    public RunE2ETests()
    {
        _project.WriteFile(".magnaflow/config.yml", "run:\n  services:\n    - name: web\n      command: web.exe\n");
        _configPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml");
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
    }

    public void Dispose()
    {
        _project.Dispose();
        try { File.Delete(_configPath); } catch { /* best effort */ }
    }

    private static string StubPath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public async Task Start_action_returns_the_stubs_output_and_exit_code()
    {
        if (!OperatingSystem.IsWindows()) return; // fixtures are .cmd stubs
        CockpitFactory.WriteConfig(_configPath, chat: null, run: (StubPath("run-stub.cmd"), TimeoutSeconds: 30), ("proj", _project.Root));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/run/web/start", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RunActionResponseDto>();

        Assert.True(result!.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("start: ok (stub)", result.Output);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Status_passes_through_the_stubs_json_output()
    {
        if (!OperatingSystem.IsWindows()) return; // fixtures are .cmd stubs
        CockpitFactory.WriteConfig(_configPath, chat: null, run: (StubPath("run-stub.cmd"), TimeoutSeconds: 30), ("proj", _project.Root));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<RunStatusDto>("/api/projects/proj/run");

        Assert.True(status!.Configured);
        var web = Assert.Single(status.Services);
        Assert.Equal("web", web.Name);
        Assert.True(web.Running);
        Assert.Equal(9999, web.Pid);
        Assert.Equal("http://localhost:5001", web.Url);
    }

    [Fact]
    public async Task A_run_command_that_cannot_be_started_surfaces_as_a_status_error_not_a_500()
    {
        var bogusCommand = Path.Combine(_project.Root, "this-command-does-not-exist.exe");
        CockpitFactory.WriteConfig(_configPath, chat: null, run: (bogusCommand, TimeoutSeconds: 30), ("proj", _project.Root));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/proj/run");

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<RunStatusDto>();

        Assert.True(status!.Configured);
        Assert.Empty(status.Services);
        Assert.NotNull(status.Error);
        Assert.Contains("could not start", status.Error);
        // WriteConfig normalizes backslashes to forward slashes when writing the YAML
        // (CockpitFactory.WriteConfig), so compare on the filename rather than the raw OS path.
        Assert.Contains("this-command-does-not-exist.exe", status.Error);
    }

    [Fact]
    public async Task Hanging_stub_is_killed_by_the_configured_timeout()
    {
        if (!OperatingSystem.IsWindows()) return; // fixtures are .cmd stubs
        CockpitFactory.WriteConfig(_configPath, chat: null, run: (StubPath("run-stub-hang.cmd"), TimeoutSeconds: 3), ("proj", _project.Root));

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsync("/api/projects/proj/run/web/start", content: null);
        stopwatch.Stop();

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RunActionResponseDto>();

        Assert.True(result!.TimedOut);
        Assert.False(result.Success);
        // 3s configured timeout; the stub sleeps ~1 hour — completing well under that proves the
        // process was actually killed, not merely outlasted.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"took {stopwatch.Elapsed} — timeout did not kill the process promptly");
    }
}
