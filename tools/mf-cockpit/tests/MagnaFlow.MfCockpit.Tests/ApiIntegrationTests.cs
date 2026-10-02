using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Tests;

public class ApiIntegrationTests : IDisposable
{
    private readonly TempProject _project = new();
    private readonly string _configPath;

    public ApiIntegrationTests()
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

    [Fact]
    public async Task Project_summary_reports_counts_per_status()
    {
        _project.WriteCmd("0001-a", status: "draft");
        _project.WriteCmd("0002-b", status: "ready");
        _project.WriteCmd("0003-c", status: "done");
        _project.WriteCmd("0004-d", status: "aborted");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<List<ProjectSummaryDto>>("/api/projects");
        var summary = Assert.Single(list!);
        Assert.Equal("proj", summary.Name);
        Assert.True(summary.Exists);
        Assert.Equal(1, summary.Counts.Draft);
        Assert.Equal(1, summary.Counts.Ready);
        Assert.Equal(1, summary.Counts.Done);
        Assert.Equal(1, summary.Counts.Aborted);

        var single = await client.GetFromJsonAsync<ProjectSummaryDto>("/api/projects/proj");
        Assert.Equal(summary.Counts, single!.Counts);
    }

    [Fact]
    public async Task Project_summary_reports_the_highest_id_command_as_latest()
    {
        _project.WriteCmd("0005-fix-lava", status: "aborted", title: "Fix lava");
        _project.WriteCmd("0005B-round2", status: "draft", title: "Round two");
        _project.WriteCmd("0006-next", status: "ready", title: "Next thing");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var summary = await client.GetFromJsonAsync<ProjectSummaryDto>("/api/projects/proj");

        Assert.NotNull(summary!.Latest);
        Assert.Equal("0006-next", summary.Latest!.Id);
        Assert.Equal("Next thing", summary.Latest.Title);
        Assert.Equal("ready", summary.Latest.Status);
    }

    [Fact]
    public async Task Project_summary_reports_the_running_command_for_the_top_bar()
    {
        _project.WriteCmd("0001-done-thing", status: "done");
        _project.WriteCmd("0002-live", status: "running", title: "Live one");
        _project.WriteCmd("0003-draft", status: "draft");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var summary = await client.GetFromJsonAsync<ProjectSummaryDto>("/api/projects/proj");

        Assert.NotNull(summary!.Running);
        Assert.Equal("0002-live", summary.Running!.Id);
        Assert.Equal("running", summary.Running.Status);
        // Latest is still the highest id regardless of what's running.
        Assert.Equal("0003-draft", summary.Latest!.Id);
    }

    [Fact]
    public async Task Project_summary_running_is_null_when_nothing_runs()
    {
        _project.WriteCmd("0001-idle", status: "done");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var summary = await client.GetFromJsonAsync<ProjectSummaryDto>("/api/projects/proj");
        Assert.Null(summary!.Running);
    }

    [Fact]
    public async Task Project_summary_latest_is_null_for_an_empty_lane()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var summary = await client.GetFromJsonAsync<ProjectSummaryDto>("/api/projects/proj");

        Assert.Null(summary!.Latest);
    }

    [Fact]
    public async Task Project_git_info_is_a_separate_slower_request()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var gitInfo = await client.GetFromJsonAsync<ProjectGitInfoDto>("/api/projects/proj/git");

        Assert.True(gitInfo!.Available);
        Assert.Equal("main", gitInfo.Branch);
        Assert.False(gitInfo.Dirty);
        Assert.Single(gitInfo.Commits);
    }

    [Fact]
    public async Task Project_git_info_on_unknown_project_is_404()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/does-not-exist/git");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_project_is_404()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Project_git_suggests_default_commit_message_from_the_single_changed_spec_file()
    {
        _project.InitGit();
        _project.WriteFile("docs/specs/frontend/page.md", "# Technical\n\nUpdated.");

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var gitInfo = await client.GetFromJsonAsync<ProjectGitInfoDto>("/api/projects/proj/git");

        Assert.True(gitInfo!.Dirty);
        Assert.Equal("page.md", gitInfo.DefaultCommitMessage);
    }

    [Fact]
    public async Task Project_git_has_no_default_commit_message_when_no_spec_file_changed()
    {
        _project.InitGit();
        _project.WriteFile("README.md", "hello");

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var gitInfo = await client.GetFromJsonAsync<ProjectGitInfoDto>("/api/projects/proj/git");

        Assert.True(gitInfo!.Dirty);
        Assert.Null(gitInfo.DefaultCommitMessage);
    }

    [Fact]
    public async Task Commit_all_commits_every_pending_change_with_the_given_message()
    {
        _project.InitGit();
        _project.WriteFile("docs/specs/frontend/page.md", "# Technical\n\nUpdated.");
        _project.WriteFile("src/Foo.cs", "// hand-edited");

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects/proj/git/commit-all", new { message = "page.md" });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CommitAllResponse>();

        Assert.True(result!.Committed);
        Assert.Null(result.Push); // no remote configured — nothing to push to, not an error
        var log = string.Join('\n', _project.GitLog());
        Assert.Contains("page.md", log);

        var gitInfo = await client.GetFromJsonAsync<ProjectGitInfoDto>("/api/projects/proj/git");
        Assert.False(gitInfo!.Dirty);
    }

    [Fact]
    public async Task Commit_all_pushes_to_the_remote_after_committing()
    {
        _project.InitGit();
        var remote = Path.Combine(Path.GetTempPath(), "mf-cockpit-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(remote);
        try
        {
            TempProject.Git(remote, "init", "-q", "--bare", "-b", "main");
            TempProject.Git(_project.Root, "remote", "add", "origin", remote);
            TempProject.Git(_project.Root, "push", "-q", "-u", "origin", "main");
            _project.WriteFile("README.md", "pushed");

            using var factory = new CockpitFactory(_configPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/projects/proj/git/commit-all", new { message = "readme" });
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CommitAllResponse>();

            Assert.True(result!.Committed);
            Assert.True(result.Push!.Success, result.Push.Output);
            Assert.Contains("readme", string.Join('\n', TempProject.GitLogAt(remote)));
        }
        finally
        {
            try { Directory.Delete(remote, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Commit_all_is_a_noop_when_nothing_is_pending()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects/proj/git/commit-all", new { message = "anything" });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CommitAllResponse>();

        Assert.False(result!.Committed);
    }

    [Fact]
    public async Task Commit_all_with_blank_message_is_400()
    {
        _project.InitGit();
        _project.WriteFile("README.md", "hello");
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects/proj/git/commit-all", new { message = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Commit_all_on_unknown_project_is_404()
    {
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects/does-not-exist/git/commit-all", new { message = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Command_detail_aggregates_lane_files_and_evidence()
    {
        _project.WriteCmd("0001-hello", status: "running", title: "Say hello");
        _project.WriteSibling("0001-hello", "pln", "# Plan\n\nGreet the user.");
        _project.WriteEvidence("0001-hello", "claude.log", "agent line 1\nagent line 2\n");
        _project.WriteEvidence("0001-hello", "session.yml", "session: abc-123\n");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var detail = await client.GetFromJsonAsync<CommandDetailDto>("/api/projects/proj/commands/0001-hello");

        Assert.Equal("0001-hello", detail!.Id);
        Assert.Equal("Say hello", detail.Title);
        Assert.Equal("running", detail.Status);
        Assert.True(detail.Cmd.Exists);
        Assert.True(detail.Pln.Exists);
        Assert.False(detail.Qa.Exists);
        Assert.Contains("Greet the user", detail.Pln.Content);
        Assert.Contains("agent line 1", detail.ClaudeLog.Lines);
        Assert.Equal("abc-123", detail.SessionId);
    }

    [Fact]
    public async Task Lane_and_detail_both_carry_the_rst_summary()
    {
        _project.WriteCmd("0001-hello", status: "done", title: "Say hello");
        _project.WriteSibling("0001-hello", "rst", "---\nsummary: The greeting is server-side now.\n---\n\n## What was done\n\nA long report.\n");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var lane = await client.GetFromJsonAsync<List<CommandSummaryDto>>("/api/projects/proj/commands");
        var detail = await client.GetFromJsonAsync<CommandDetailDto>("/api/projects/proj/commands/0001-hello");

        Assert.Equal("The greeting is server-side now.", Assert.Single(lane!).Summary);
        Assert.Equal("The greeting is server-side now.", detail!.Summary);
    }

    [Fact]
    public async Task Unknown_command_id_is_404()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/proj/commands/9999-nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Watch_endpoint_returns_log_tail_and_lock_presence()
    {
        _project.WriteFile(".magnaflow/mf-watch.log", "poll 1\npoll 2\npoll 3");
        _project.WriteFile(".magnaflow/mf-watch.lock", "");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var watch = await client.GetFromJsonAsync<WatchDto>("/api/projects/proj/watch?tail=2");

        Assert.True(watch!.LockPresent);
        Assert.Equal(2, watch.Log.Lines.Count);
        Assert.Equal("poll 3", watch.Log.Lines[^1]);
    }

    [Fact]
    public async Task Specs_browse_lists_directory_and_returns_file_content()
    {
        _project.WriteSpec("_overview.md", "# Technical\n\nOverview.");
        _project.WriteSpec("sub/page.md", "# Technical\n\nA page.");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        using var dirDoc = JsonDocument.Parse(await client.GetStringAsync("/api/projects/proj/specs"));
        var names = dirDoc.RootElement.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("_overview.md", names);
        Assert.Contains("sub", names);

        using var fileDoc = JsonDocument.Parse(await client.GetStringAsync("/api/projects/proj/specs/sub/page.md"));
        Assert.Equal("file", fileDoc.RootElement.GetProperty("type").GetString());
        Assert.Contains("A page.", fileDoc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Specs_browse_rejects_path_escape_and_never_leaks_outside_content()
    {
        var secretPath = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", "secret-" + Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(secretPath)!);
        const string secretMarker = "TOP-SECRET-OUTSIDE-ROOT";
        File.WriteAllText(secretPath, secretMarker);
        try
        {
            _project.WriteSpec("_overview.md", "# Technical\n\nOverview.");
            _project.InitGit();

            using var factory = new CockpitFactory(_configPath);
            using var client = factory.CreateClient();

            // A rooted/drive-letter segment reaches SpecsBrowser un-mangled by any URL
            // normalization layer and is the deterministic way to exercise the guard end-to-end.
            var escapePath = Uri.EscapeDataString(secretPath).Replace("%5C", "/").Replace("%2F", "/");
            var response = await client.GetAsync($"/api/projects/proj/specs/{escapePath}");

            Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(secretMarker, body);
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    [Fact]
    public async Task Specs_browse_unknown_path_is_404()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/projects/proj/specs/does/not/exist.md");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_draft_picks_next_free_number_and_commits()
    {
        _project.WriteCmd("0001-existing");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects/proj/commands", new { title = "New idea", body = "## Goal\nDo it." });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateDraftResponse>();

        Assert.Equal("0002-new-idea", created!.Id);
        Assert.Contains("status: draft", File.ReadAllText(created.Path));
        Assert.Contains("cockpit: create draft 0002-new-idea", string.Join('\n', _project.GitLog()));
    }

    [Fact]
    public async Task Create_draft_with_blank_title_is_400()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/projects/proj/commands", new { title = "   ", body = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ready_flip_refuses_non_draft_status_with_409()
    {
        _project.WriteCmd("0001-hello", status: "ready");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/commands/0001-hello/ready", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("status: ready", File.ReadAllText(_project.CmdPath("0001-hello")));
    }

    [Fact]
    public async Task Ready_flip_on_draft_succeeds_and_commits()
    {
        _project.WriteCmd("0001-hello", status: "draft");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/commands/0001-hello/ready", content: null);

        response.EnsureSuccessStatusCode();
        Assert.Contains("status: ready", File.ReadAllText(_project.CmdPath("0001-hello")));
        Assert.Contains("cockpit: ready 0001-hello", string.Join('\n', _project.GitLog()));
    }

    [Fact]
    public async Task Ready_flip_on_unknown_id_is_404()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/projects/proj/commands/9999-nope/ready", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string WriteParentCmd(TempProject project, string id, string status = "done")
    {
        Directory.CreateDirectory(project.PromptsDir);
        var (num, name) = TempProject.SplitId(id);
        var path = Path.Combine(project.PromptsDir, $"{num}-cmd-{name}.md");
        File.WriteAllText(path, $"""
            ---
            title: Fix the lava colors
            status: {status}
            branch: task/{id}
            attempts: 1
            ---

            ## Goal
            Fix it.
            """);
        return path;
    }

    [Fact]
    public async Task Follow_up_creates_suffixed_draft_with_resume_from_sessions_and_commits()
    {
        WriteParentCmd(_project, "0005-fix-lava");
        _project.WriteEvidence("0005-fix-lava", "session.yml", "session: sess-abc-123\n");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/projects/proj/commands/0005-fix-lava/follow-up",
            new { feedback = "this line should be orange, not red" });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<FollowUpResponse>();

        Assert.Equal("0005B-fix-lava", result!.Id);
        Assert.True(result.ResumeSet);
        Assert.Null(result.Warning);

        var content = File.ReadAllText(result.Path);
        Assert.Contains("status: draft", content);
        Assert.Contains("resume: sess-abc-123", content);
        Assert.Contains("branch: task/0005-fix-lava", content);
        Assert.Contains("cockpit: create follow-up draft 0005B-fix-lava (from 0005-fix-lava)", string.Join('\n', _project.GitLog()));
    }

    [Fact]
    public async Task Follow_up_warns_and_omits_resume_when_parent_has_no_session()
    {
        WriteParentCmd(_project, "0005-fix-lava");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/projects/proj/commands/0005-fix-lava/follow-up",
            new { feedback = "feedback text" });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<FollowUpResponse>();

        Assert.False(result!.ResumeSet);
        Assert.NotNull(result.Warning);
        Assert.DoesNotContain("resume:", File.ReadAllText(result.Path));
    }

    [Fact]
    public async Task Follow_up_on_unknown_parent_is_409()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/projects/proj/commands/9999-nope/follow-up",
            new { feedback = "feedback text" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Config_endpoint_reports_chat_enabled()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var config = await client.GetFromJsonAsync<CockpitConfigDto>("/api/config");

        Assert.True(config!.ChatEnabled);
    }

    [Fact]
    public async Task Config_endpoint_reports_the_loaded_config_path_and_effective_commands()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var config = await client.GetFromJsonAsync<CockpitConfigDto>("/api/config");

        Assert.Equal(_configPath, config!.ConfigPath);
        Assert.Equal("mf-run", config.RunCommand);
        Assert.Equal("claude", config.ChatCommand);
    }

    [Fact]
    public async Task Cockpit_log_endpoint_reports_the_startup_line()
    {
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        var tail = await client.GetFromJsonAsync<LogTailDto>("/api/cockpit/log");

        Assert.True(tail!.Exists);
        Assert.Contains(tail.Lines, l => l.Contains("startup:") && l.Contains(_configPath));
    }

    [Fact]
    public async Task Events_stream_emits_after_a_lane_file_change()
    {
        _project.WriteCmd("0001-hello", status: "ready");
        _project.InitGit();

        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Give the FileSystemWatcher a moment to be wired up, then mutate a lane file.
        await Task.Delay(300, cts.Token);
        File.WriteAllText(_project.CmdPath("0001-hello"), TempProject.CmdMarkdown("done"));

        string? line;
        var sawLaneEventForProject = false;
        while (!sawLaneEventForProject && (line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            if (!line.StartsWith("data: "))
                continue;
            using var doc = JsonDocument.Parse(line["data: ".Length..]);
            if (doc.RootElement.GetProperty("project").GetString() == "proj" && doc.RootElement.GetProperty("kind").GetString() == "lane")
                sawLaneEventForProject = true;
        }

        Assert.True(sawLaneEventForProject);
    }

    [Fact]
    public async Task Events_stream_sends_a_heartbeat_ping_on_an_idle_connection()
    {
        // The heartbeat (ontwerp-v0.5.md item 1) is what lets a client tell a live-but-idle stream
        // from a dead socket. ~15s server interval, so allow generous time.
        _project.InitGit();
        using var factory = new CockpitFactory(_configPath);
        using var client = factory.CreateClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        string? line;
        var sawPing = false;
        while (!sawPing && (line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            if (!line.StartsWith("data: "))
                continue;
            using var doc = JsonDocument.Parse(line["data: ".Length..]);
            if (doc.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() == "ping")
                sawPing = true;
        }

        Assert.True(sawPing);
    }
}
