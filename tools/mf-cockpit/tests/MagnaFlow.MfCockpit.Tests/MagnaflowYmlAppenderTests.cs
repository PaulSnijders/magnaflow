using MagnaFlow.MfCockpit.Config;

namespace MagnaFlow.MfCockpit.Tests;

public class MagnaflowYmlAppenderComposeTests
{
    private static readonly ProjectEntry Entry = new() { Name = "newproj", Path = @"C:\GIT\newproj" };

    [Fact]
    public void Shape1_appends_to_an_existing_block_list_after_the_last_entry_preserving_the_rest_byte_for_byte()
    {
        var original = """
            cockpit:
              port: 6000
              projects:
                - name: demo
                  path: C:/demo
              chat:
                command: claude
            """.Replace("\r\n", "\n") + "\n";

        var edited = MagnaflowYmlAppender.ComposeAppendedContent(original, Entry);

        Assert.Equal("""
            cockpit:
              port: 6000
              projects:
                - name: demo
                  path: C:/demo
                - name: newproj
                  path: C:/GIT/newproj
              chat:
                command: claude
            """.Replace("\r\n", "\n") + "\n", edited);
    }

    [Fact]
    public void Shape1_appends_after_the_only_entry_when_the_projects_block_is_the_last_thing_in_the_file()
    {
        var original = "cockpit:\n  projects:\n    - name: demo\n      path: C:/demo\n";

        var edited = MagnaflowYmlAppender.ComposeAppendedContent(original, Entry);

        Assert.Equal("cockpit:\n  projects:\n    - name: demo\n      path: C:/demo\n    - name: newproj\n      path: C:/GIT/newproj\n", edited);
    }

    [Fact]
    public void Shape2_replaces_an_inline_empty_list_with_a_block_list()
    {
        var original = "cockpit:\n  port: 6000\n  projects: []\n  bind: 0.0.0.0\n";

        var edited = MagnaflowYmlAppender.ComposeAppendedContent(original, Entry);

        Assert.Equal("cockpit:\n  port: 6000\n  projects:\n    - name: newproj\n      path: C:/GIT/newproj\n  bind: 0.0.0.0\n", edited);
    }

    [Fact]
    public void Shape3_inserts_a_projects_key_into_a_cockpit_section_that_lacks_one()
    {
        var original = "cockpit:\n  port: 6000\n  bind: 0.0.0.0\n";

        var edited = MagnaflowYmlAppender.ComposeAppendedContent(original, Entry);

        Assert.Equal("cockpit:\n  projects:\n    - name: newproj\n      path: C:/GIT/newproj\n  port: 6000\n  bind: 0.0.0.0\n", edited);
    }

    [Fact]
    public void Shape4_appends_a_whole_new_cockpit_section_when_none_exists_preserving_the_other_tools_section()
    {
        var original = "watch:\n  git_sync: true\n  # comment about git_sync\n";

        var edited = MagnaflowYmlAppender.ComposeAppendedContent(original, Entry);

        Assert.Equal("watch:\n  git_sync: true\n  # comment about git_sync\n\ncockpit:\n  projects:\n    - name: newproj\n      path: C:/GIT/newproj\n", edited);
    }

    [Fact]
    public void Shape4_on_a_completely_empty_file_produces_just_the_cockpit_section()
    {
        var edited = MagnaflowYmlAppender.ComposeAppendedContent("", Entry);

        Assert.Equal("cockpit:\n  projects:\n    - name: newproj\n      path: C:/GIT/newproj\n", edited);
    }

    [Fact]
    public void Comments_inside_the_projects_block_survive_byte_for_byte()
    {
        var original = "cockpit:\n  projects:\n    # the demo project\n    - name: demo\n      path: C:/demo\n  chat:\n    command: claude\n";

        var edited = MagnaflowYmlAppender.ComposeAppendedContent(original, Entry);

        Assert.Contains("# the demo project", edited);
        Assert.Equal(
            "cockpit:\n  projects:\n    # the demo project\n    - name: demo\n      path: C:/demo\n    - name: newproj\n      path: C:/GIT/newproj\n  chat:\n    command: claude\n",
            edited);
    }

    [Fact]
    public void Windows_backslash_paths_are_written_with_forward_slashes()
    {
        var edited = MagnaflowYmlAppender.ComposeAppendedContent("", new ProjectEntry { Name = "x", Path = @"C:\GIT\a\b" });

        Assert.Contains("path: C:/GIT/a/b", edited);
    }

    [Fact]
    public void Crlf_input_is_preserved_as_the_line_ending()
    {
        var original = "cockpit:\r\n  projects: []\r\n";

        var edited = MagnaflowYmlAppender.ComposeAppendedContent(original, Entry);

        Assert.Equal("cockpit:\r\n  projects:\r\n    - name: newproj\r\n      path: C:/GIT/newproj\r\n", edited);
    }
}

public class MagnaflowYmlRemoveComposeTests
{
    [Fact]
    public void Removes_one_entry_from_a_cockpit_block_preserving_the_others_and_surrounding_sections()
    {
        var original = """
            cockpit:
              projects:
                - name: alpha
                  path: C:/alpha
                - name: beta
                  path: C:/beta
                - name: gamma
                  path: C:/gamma
              chat:
                command: claude
            """.Replace("\r\n", "\n") + "\n";

        var edited = MagnaflowYmlAppender.ComposeRemovedContent(original, "beta");

        Assert.Equal("""
            cockpit:
              projects:
                - name: alpha
                  path: C:/alpha
                - name: gamma
                  path: C:/gamma
              chat:
                command: claude
            """.Replace("\r\n", "\n") + "\n", edited);
    }

    [Fact]
    public void Removing_the_only_entry_leaves_an_empty_projects_block()
    {
        var original = "cockpit:\n  projects:\n    - name: only\n      path: C:/only\n";

        var edited = MagnaflowYmlAppender.ComposeRemovedContent(original, "only");

        Assert.Equal("cockpit:\n  projects:\n", edited);
    }

    [Fact]
    public void Removes_from_a_root_level_legacy_projects_block()
    {
        var original = "projects:\n  - name: alpha\n    path: C:/alpha\n  - name: beta\n    path: C:/beta\n";

        var edited = MagnaflowYmlAppender.ComposeRemovedContent(original, "alpha");

        Assert.Equal("projects:\n  - name: beta\n    path: C:/beta\n", edited);
    }

    [Fact]
    public void Comments_inside_the_projects_block_survive_a_removal()
    {
        var original = "cockpit:\n  projects:\n    # keep me\n    - name: alpha\n      path: C:/alpha\n    - name: beta\n      path: C:/beta\n";

        var edited = MagnaflowYmlAppender.ComposeRemovedContent(original, "beta");

        Assert.Equal("cockpit:\n  projects:\n    # keep me\n    - name: alpha\n      path: C:/alpha\n", edited);
    }

    [Fact]
    public void A_name_matching_a_non_project_list_item_is_not_removed()
    {
        // A template happens to share the name — only the entry under `projects:` may be removed.
        var original = "cockpit:\n  projects:\n    - name: alpha\n      path: C:/alpha\n  new_project:\n    templates:\n      - name: alpha\n        type: copy\n";

        var edited = MagnaflowYmlAppender.ComposeRemovedContent(original, "alpha");

        // Only the projects entry goes; the now-empty `projects:` key line stays, and the identically
        // named template is untouched.
        Assert.Equal("cockpit:\n  projects:\n  new_project:\n    templates:\n      - name: alpha\n        type: copy\n", edited);
    }
}

public class MagnaflowYmlRemoveTests
{
    [Fact]
    public async Task Removes_an_entry_and_the_survivor_round_trips()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml",
            "cockpit:\n  projects:\n    - name: alpha\n      path: C:/alpha\n    - name: beta\n      path: C:/beta\n");
        var (config, _, _) = CockpitConfig.Load(path);

        var result = await MagnaflowYmlAppender.RemoveProjectAsync(config!, "alpha");

        Assert.Equal(MagnaflowYmlAppender.RemoveOutcome.Ok, result.Outcome);
        var (reloaded, error, _) = CockpitConfig.Load(path);
        Assert.Null(error);
        var survivor = Assert.Single(reloaded!.Projects);
        Assert.Equal("beta", survivor.Name);
        Assert.False(File.Exists(path + ".bak"), ".bak is cleaned up after a successful removal");
    }

    [Fact]
    public async Task Refuses_to_remove_from_the_legacy_mf_cockpit_yml_filename()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-cockpit.yml", "projects:\n  - name: alpha\n    path: C:/alpha\n");
        var (config, _, _) = CockpitConfig.Load(path, isLegacyFileName: true);

        var result = await MagnaflowYmlAppender.RemoveProjectAsync(config!, "alpha");

        Assert.Equal(MagnaflowYmlAppender.RemoveOutcome.LegacyFileRefused, result.Outcome);
        Assert.Equal("projects:\n  - name: alpha\n    path: C:/alpha\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task A_corrupted_removal_that_drops_a_sibling_restores_the_backup_and_reports_VerifyFailed()
    {
        using var project = new TempProject();
        var original = "cockpit:\n  projects:\n    - name: alpha\n      path: C:/alpha\n    - name: beta\n      path: C:/beta\n";
        var path = project.WriteFile("magnaflow.yml", original);
        var (config, _, _) = CockpitConfig.Load(path);

        // The broken composer removes BOTH entries — the target's sibling would be lost, which the
        // reparse-verify must catch and roll back.
        var result = await MagnaflowYmlAppender.RemoveProjectAsync(
            config!, "alpha", userConfigDirectory: null,
            composer: (_, _) => "cockpit:\n  projects:\n");

        Assert.Equal(MagnaflowYmlAppender.RemoveOutcome.VerifyFailed, result.Outcome);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".bak"), ".bak is cleaned up after a restore too");
    }
}

public class MagnaflowYmlAppenderAppendTests
{
    // A rooted path on the running OS: a Windows drive path like C:\x is relative on Linux, so the
    // appender's reparse-verify (Path.GetFullPath) would resolve it differently there.
    private static string RootedPath(string name) =>
        OperatingSystem.IsWindows() ? $@"C:\{name}" : $"/tmp/{name}";

    [Fact]
    public async Task Appends_to_an_existing_sectioned_magnaflow_yml_next_to_the_binary()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", "cockpit:\n  projects: []\n");
        var (config, _, _) = CockpitConfig.Load(path);

        var result = await MagnaflowYmlAppender.AppendProjectAsync(config!, new ProjectEntry { Name = "x", Path = RootedPath("x") });

        Assert.Equal(MagnaflowYmlAppender.Outcome.Ok, result.Outcome);
        Assert.Equal(path, result.Path);
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("- name: x", content);
        Assert.False(File.Exists(path + ".bak"), ".bak is cleaned up after a successful append");
    }

    [Fact]
    public async Task Refuses_to_append_to_the_legacy_mf_cockpit_yml_filename()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-cockpit.yml", "port: 6000\n");
        var (config, _, _) = CockpitConfig.Load(path, isLegacyFileName: true);

        var result = await MagnaflowYmlAppender.AppendProjectAsync(config!, new ProjectEntry { Name = "x", Path = RootedPath("x") });

        Assert.Equal(MagnaflowYmlAppender.Outcome.LegacyFileRefused, result.Outcome);
        Assert.Contains("magnaflow.yml", result.Error);
        // the legacy file itself is never touched
        Assert.Equal("port: 6000\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Creates_magnaflow_yml_in_the_user_config_dir_when_nothing_was_found_anywhere()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();
        // Mirrors CockpitConfig.LocatePath's own "nothing found anywhere" sentinel: the primary
        // (next-to-binary) path, non-legacy, not present on disk.
        var (locatedPath, isLegacy) = CockpitConfig.LocatePath([], baseDir.Root, userDir.Root);
        var (config, _, _) = CockpitConfig.Load(locatedPath, isLegacy);

        var result = await MagnaflowYmlAppender.AppendProjectAsync(config!, new ProjectEntry { Name = "x", Path = RootedPath("x") }, userDir.Root);

        Assert.Equal(MagnaflowYmlAppender.Outcome.Ok, result.Outcome);
        var expectedPath = Path.Combine(userDir.Root, CockpitConfig.FileName);
        Assert.Equal(expectedPath, result.Path);
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(Path.Combine(baseDir.Root, CockpitConfig.FileName)), "the next-to-binary location is never written when nothing was found there");
        Assert.Contains("- name: x", await File.ReadAllTextAsync(expectedPath));
    }

    [Fact]
    public async Task Round_trips_the_new_entry_verified_by_reparsing()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", "cockpit:\n  port: 5210\n");
        var (config, _, _) = CockpitConfig.Load(path);

        await MagnaflowYmlAppender.AppendProjectAsync(config!, new ProjectEntry { Name = "roundtrip", Path = RootedPath("roundtrip") });

        var (reloaded, error, _) = CockpitConfig.Load(path);
        Assert.Null(error);
        var entry = Assert.Single(reloaded!.Projects);
        Assert.Equal("roundtrip", entry.Name);
    }

    [Fact]
    public async Task A_corrupted_append_restores_the_backup_and_reports_VerifyFailed()
    {
        using var project = new TempProject();
        var original = "cockpit:\n  port: 5210\n  projects: []\n";
        var path = project.WriteFile("magnaflow.yml", original);
        var (config, _, _) = CockpitConfig.Load(path);

        var result = await MagnaflowYmlAppender.AppendProjectAsync(
            config!, new ProjectEntry { Name = "x", Path = RootedPath("x") }, userConfigDirectory: null,
            composer: (_, _) => "not: [valid, yaml, this is broken {{{");

        Assert.Equal(MagnaflowYmlAppender.Outcome.VerifyFailed, result.Outcome);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".bak"), ".bak is cleaned up after a restore too");
    }

    [Fact]
    public async Task A_corrupted_append_to_a_brand_new_file_deletes_it_rather_than_leaving_broken_content()
    {
        using var project = new TempProject();
        var path = Path.Combine(project.Root, "magnaflow.yml");
        var (config, _, _) = CockpitConfig.Load(path);

        var result = await MagnaflowYmlAppender.AppendProjectAsync(
            config!, new ProjectEntry { Name = "x", Path = RootedPath("x") }, userConfigDirectory: null,
            composer: (_, _) => "not: [valid, yaml, this is broken {{{");

        Assert.Equal(MagnaflowYmlAppender.Outcome.VerifyFailed, result.Outcome);
        Assert.False(File.Exists(path), "no file existed before the append, so a failed append must leave none behind");
    }
}
