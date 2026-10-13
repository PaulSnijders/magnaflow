using System.Net;
using System.Net.Http.Json;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Live;
using MagnaFlow.MfCockpit.Models;
using MagnaFlow.MfCockpit.Watch;
using Microsoft.Extensions.DependencyInjection;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>The Actions card's API (docs/specs/cockpit/project.md#actions).</summary>
public class ActionsApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;
    private readonly FakeWatchControl _watchControl = new() { RunningResult = true };
    private readonly FakeGitClient _git = new();

    public ActionsApiIntegrationTests()
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

    private CockpitFactory Factory() => new(_configPath, services =>
    {
        services.AddSingleton<IWatchControl>(_watchControl);
        services.AddSingleton<IGitClient>(_git);
    });

    private string ActionsDir => Path.Combine(_project.MagnaflowDir, "actions");

    private void WriteActions(string yaml)
    {
        Directory.CreateDirectory(_project.MagnaflowDir);
        File.WriteAllText(Path.Combine(_project.MagnaflowDir, "actions.yml"), yaml);
    }

    private const string TwoActions = """
        actions:
          hello:
            description: Say hello
            script: tools/hello
            every_minutes: 2
          fix:
            prompt: fix it
        """;

    [Fact]
    public async Task Without_actions_yml_the_list_says_so()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var dto = await client.GetFromJsonAsync<ActionsDto>("/api/projects/proj/actions");

        Assert.False(dto!.Exists);
        Assert.Empty(dto.Actions);
    }

    [Fact]
    public async Task The_list_shows_definitions_last_run_and_pending_requests()
    {
        WriteActions(TwoActions);
        Directory.CreateDirectory(ActionsDir);
        File.WriteAllText(Path.Combine(ActionsDir, "hello.last.yml"),
            "run_at: 2026-10-13T09:00:00+02:00\ntrigger: schedule\nresult: \"ok\"\nduration_seconds: 4\n");
        File.WriteAllText(Path.Combine(ActionsDir, "fix.request"), "");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var dto = await client.GetFromJsonAsync<ActionsDto>("/api/projects/proj/actions");

        Assert.True(dto!.Exists);
        Assert.Null(dto.Error);
        Assert.False(dto.Ignored);
        Assert.Equal(["hello", "fix"], dto.Actions.Select(a => a.Name));
        var hello = dto.Actions[0];
        Assert.Equal(("Say hello", "script", "every 2 min", false), (hello.Description, hello.Kind, hello.Schedule, hello.Pending));
        Assert.Equal(("ok", 4), (hello.Last!.Result, hello.Last.DurationSeconds));
        Assert.Equal(("manual", true), (dto.Actions[1].Schedule, dto.Actions[1].Pending));
        Assert.Null(dto.Actions[1].Last);
    }

    [Fact]
    public async Task The_list_reports_a_parse_error_and_an_ignored_file()
    {
        WriteActions("actions:\n  hello:\n    script: x\n    prompt: y\n");
        _git.Ignored = true;
        using var factory = Factory();
        using var client = factory.CreateClient();

        var dto = await client.GetFromJsonAsync<ActionsDto>("/api/projects/proj/actions");

        Assert.True(dto!.Exists);
        Assert.Contains("action 'hello': script/prompt are both set", dto.Error);
        Assert.True(dto.Ignored);
        Assert.Empty(dto.Actions);
    }

    [Fact]
    public async Task Run_writes_the_request_and_the_wake_file()
    {
        WriteActions(TwoActions);
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/actions/fix/run", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(ActionsDir, "fix.request")));
        Assert.True(File.Exists(MfWatchWakeFile.PathFor(_project.Root)));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("..%2Fx")]
    public async Task Run_is_404_for_a_name_not_in_actions_yml(string name)
    {
        WriteActions(TwoActions);
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/projects/proj/actions/{name}/run", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(Directory.Exists(ActionsDir));
    }

    [Fact]
    public async Task Run_never_reaches_a_file_for_a_path_like_name_with_a_literal_slash()
    {
        WriteActions(TwoActions);
        using var factory = Factory();
        using var client = factory.CreateClient();

        // The client collapses the dots, so this never even matches the run route.
        var response = await client.PostAsync("/api/projects/proj/actions/../x/run", content: null);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.False(Directory.Exists(ActionsDir));
    }

    [Fact]
    public async Task Run_is_409_without_a_watcher()
    {
        WriteActions(TwoActions);
        _watchControl.RunningResult = false;
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/actions/fix/run", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(ActionsDir, "fix.request")));
        Assert.False(File.Exists(MfWatchWakeFile.PathFor(_project.Root)));
    }

    [Fact]
    public async Task Run_is_409_while_a_request_is_pending()
    {
        WriteActions(TwoActions);
        Directory.CreateDirectory(ActionsDir);
        File.WriteAllText(Path.Combine(ActionsDir, "fix.request"), "");
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/actions/fix/run", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_log_tail_is_served_for_a_known_action_only()
    {
        WriteActions(TwoActions);
        Directory.CreateDirectory(ActionsDir);
        File.WriteAllLines(Path.Combine(ActionsDir, "hello.log"), Enumerable.Range(1, 150).Select(i => $"line {i}"));
        using var factory = Factory();
        using var client = factory.CreateClient();

        var tail = await client.GetFromJsonAsync<LogTailDto>("/api/projects/proj/actions/hello/log");
        var unknown = await client.GetAsync("/api/projects/proj/actions/nope/log");

        Assert.Equal(100, tail!.Lines.Count);
        Assert.Contains("line 150", tail.Lines);
        Assert.True(tail.Truncated);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    [InlineData(".magnaflow/actions.yml", "actions")]
    [InlineData(".magnaflow/actions", "actions")]
    [InlineData(".magnaflow/actions/hello.log", "actions")]
    [InlineData(".magnaflow/actions/hello.request", "actions")]
    [InlineData(".magnaflow/0001-x/claude.log", "evidence")]
    [InlineData(".magnaflow/mf-watch.log", "watch")]
    public void Event_kinds_map_actions_without_also_raising_evidence(string relative, string kind)
    {
        var full = Path.Combine(_project.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(kind, ProjectWatcher.ClassifyMagnaflow(full));
    }
}
