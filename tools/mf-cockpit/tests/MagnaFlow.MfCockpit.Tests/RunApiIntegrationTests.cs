using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// Endpoint-wiring tests for the Run card's API (ontwerp-v0.3.md "The Run card" / "API"), against a
/// FakeRunClient so they run fast and deterministic — the small number of scenarios that must
/// exercise a real mf-run.command spawn (start output, timeout kill) live in RunE2ETests instead.
/// </summary>
public class RunApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;
    private readonly FakeRunClient _runClient = new();

    public RunApiIntegrationTests()
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

    private CockpitFactory Factory() => new(_configPath, services => services.AddSingleton<IRunClient>(_runClient));

    private void WriteRunConfig(string servicesYaml) =>
        _project.WriteFile(".magnaflow/config.yml", $"run:\n  services:\n{servicesYaml}");

    [Fact]
    public async Task Status_reports_configured_false_when_project_has_no_config_file()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<RunStatusDto>("/api/projects/proj/run");

        Assert.False(status!.Configured);
        Assert.Empty(status.Services);
        Assert.Empty(_runClient.StartCalls);
    }

    [Fact]
    public async Task Status_reports_configured_false_when_config_has_no_run_block()
    {
        _project.WriteFile(".magnaflow/config.yml", "build:\n  command: dotnet build\n");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<RunStatusDto>("/api/projects/proj/run");

        Assert.False(status!.Configured);
    }

    [Fact]
    public async Task Status_passes_through_services_from_mf_run_json()
    {
        WriteRunConfig("    - name: web\n      command: web.exe\n    - name: api\n      command: api.exe\n");
        _runClient.StatusOutput = """[{"name":"web","running":true,"pid":4242,"url":"http://localhost:5000"},{"name":"api","running":false}]""";

        using var factory = Factory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<RunStatusDto>("/api/projects/proj/run");

        Assert.True(status!.Configured);
        Assert.Null(status.Error);
        Assert.Equal(2, status.Services.Count);
        var web = status.Services.Single(s => s.Name == "web");
        Assert.True(web.Running);
        Assert.Equal(4242, web.Pid);
        Assert.Equal("http://localhost:5000", web.Url);
        var api = status.Services.Single(s => s.Name == "api");
        Assert.False(api.Running);
        Assert.Null(api.Pid);
    }

    [Fact]
    public async Task Status_surfaces_an_error_when_mf_run_output_is_not_json()
    {
        WriteRunConfig("    - name: web\n      command: web.exe\n");
        _runClient.StatusOutput = "mf-run: unknown command 'status'";
        _runClient.StatusExitCode = 2;

        using var factory = Factory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<RunStatusDto>("/api/projects/proj/run");

        Assert.True(status!.Configured);
        Assert.Empty(status.Services);
        Assert.NotNull(status.Error);
        Assert.Contains("exit 2", status.Error);
    }

    [Fact]
    public async Task Start_all_dispatches_with_null_service()
    {
        WriteRunConfig("    - name: web\n      command: web.exe\n");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/run/start", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RunActionResponseDto>();

        Assert.True(result!.Success);
        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(_runClient.StartCalls);
        Assert.Null(call.Service);
    }

    [Fact]
    public async Task Stop_all_dispatches_with_null_service()
    {
        WriteRunConfig("    - name: web\n      command: web.exe\n");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/run/stop", content: null);
        response.EnsureSuccessStatusCode();

        var call = Assert.Single(_runClient.StopCalls);
        Assert.Null(call.Service);
    }

    [Fact]
    public async Task Start_one_service_dispatches_with_the_service_name()
    {
        WriteRunConfig("    - name: web\n      command: web.exe\n    - name: api\n      command: api.exe\n");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/run/web/start", content: null);
        response.EnsureSuccessStatusCode();

        var call = Assert.Single(_runClient.StartCalls);
        Assert.Equal("web", call.Service);
    }

    [Fact]
    public async Task Restart_one_service_dispatches_with_the_service_name()
    {
        WriteRunConfig("    - name: web\n      command: web.exe\n");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/run/web/restart", content: null);
        response.EnsureSuccessStatusCode();

        var call = Assert.Single(_runClient.RestartCalls);
        Assert.Equal("web", call.Service);
    }

    [Fact]
    public async Task Action_on_unknown_service_name_is_404()
    {
        WriteRunConfig("    - name: web\n      command: web.exe\n");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/run/does-not-exist/start", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_runClient.StartCalls);
    }

    [Fact]
    public async Task Action_on_a_service_when_project_has_no_run_block_is_404()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/run/web/start", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Run_endpoints_on_unknown_project_are_404()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/does-not-exist/run")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/projects/does-not-exist/run/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/projects/does-not-exist/run/web/start", null)).StatusCode);
    }

    [Fact]
    public async Task Log_tail_returns_bounded_lines_for_an_existing_service_log()
    {
        _project.WriteFile(".magnaflow/run/web.log", "line 1\nline 2\nline 3");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var tail = await client.GetFromJsonAsync<LogTailDto>("/api/projects/proj/run/web/log?tail=2");

        Assert.True(tail!.Exists);
        Assert.Equal(2, tail.Lines.Count);
        Assert.Equal("line 3", tail.Lines[^1]);
    }

    [Fact]
    public async Task Log_tail_reports_not_exists_for_a_service_with_no_log_yet()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var tail = await client.GetFromJsonAsync<LogTailDto>("/api/projects/proj/run/web/log");

        Assert.False(tail!.Exists);
        Assert.Empty(tail.Lines);
    }
}
