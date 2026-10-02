using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Watch;
using Microsoft.Extensions.DependencyInjection;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// Endpoint-wiring tests for the watch toggle (Watch/IWatchControl.cs), against a FakeWatchControl
/// so they run fast, deterministic, and OS-independent — same role RunApiIntegrationTests plays for
/// the Run card.
/// </summary>
public class WatchApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;
    private readonly FakeWatchControl _watchControl = new();

    public WatchApiIntegrationTests()
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

    private CockpitFactory Factory() => new(_configPath, services => services.AddSingleton<IWatchControl>(_watchControl));

    [Fact]
    public async Task Status_reports_running_from_the_watch_control()
    {
        _watchControl.RunningResult = true;
        using var factory = Factory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<WatchDto>("/api/projects/proj/watch");

        Assert.True(status!.Running);
    }

    [Fact]
    public async Task Status_reports_not_running_when_no_lock()
    {
        _watchControl.RunningResult = false;
        using var factory = Factory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<WatchDto>("/api/projects/proj/watch");

        Assert.False(status!.Running);
    }

    [Fact]
    public async Task Start_dispatches_to_the_watch_control_with_the_project_path()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/watch/start", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WatchActionResponseDto>();

        Assert.True(result!.Success);
        var call = Assert.Single(_watchControl.StartCalls);
        Assert.Equal(_project.Root, call);
    }

    [Fact]
    public async Task Stop_dispatches_to_the_watch_control_with_the_project_path()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/watch/stop", content: null);
        response.EnsureSuccessStatusCode();

        var call = Assert.Single(_watchControl.StopCalls);
        Assert.Equal(_project.Root, call);
    }

    [Fact]
    public async Task Start_surfaces_a_failure_from_the_watch_control()
    {
        _watchControl.StartResult = new WatchControlResult(false, "failed to start 'mf-watch'");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/watch/start", content: null);
        var result = await response.Content.ReadFromJsonAsync<WatchActionResponseDto>();

        Assert.False(result!.Success);
        Assert.Equal("failed to start 'mf-watch'", result.Error);
    }

    [Fact]
    public async Task Check_now_writes_the_wake_file()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/watch/check-now", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WatchActionResponseDto>();

        Assert.True(result!.Success);
        Assert.True(File.Exists(MfWatchWakeFile.PathFor(_project.Root)));
        // No process logic: the wake is the file, so the watch control is not consulted at all.
        Assert.Empty(_watchControl.StartCalls);
        Assert.Empty(_watchControl.StopCalls);
    }

    [Fact]
    public async Task Check_now_twice_leaves_one_pending_wake()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        (await client.PostAsync("/api/projects/proj/watch/check-now", content: null)).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/projects/proj/watch/check-now", content: null)).EnsureSuccessStatusCode();

        Assert.True(File.Exists(MfWatchWakeFile.PathFor(_project.Root)));
    }

    [Fact]
    public async Task Watch_endpoints_on_unknown_project_are_404()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/does-not-exist/watch")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/projects/does-not-exist/watch/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/projects/does-not-exist/watch/stop", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/projects/does-not-exist/watch/check-now", null)).StatusCode);
    }
}
