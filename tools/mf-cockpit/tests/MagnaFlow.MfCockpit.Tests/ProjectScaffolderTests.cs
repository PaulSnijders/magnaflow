using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Projects;
using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Tests;

public class ProjectScaffolderValidationTests
{
    private static ProjectRegistry Registry(params (string Name, string Path)[] entries) =>
        new(entries.Select(e => new ProjectEntry { Name = e.Name, Path = e.Path }));

    private static ProjectScaffolder Scaffolder(NewProjectConfig? config = null, TempProject? project = null) =>
        new(new FakeProcessRunner(), new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()),
            config ?? new NewProjectConfig { Root = project?.Root ?? @"C:\GIT" });

    [Fact]
    public void Root_not_configured_is_rejected_before_anything_else_that_needs_it()
    {
        var scaffolder = Scaffolder(new NewProjectConfig { Root = null });

        var result = scaffolder.Validate("demo", null, Registry());

        Assert.Equal(ProjectScaffolder.ValidationOutcome.RootNotConfigured, result.Outcome);
    }

    [Fact]
    public void Empty_name_is_rejected()
    {
        var result = Scaffolder().Validate("   ", null, Registry());

        Assert.Equal(ProjectScaffolder.ValidationOutcome.NameRequired, result.Outcome);
    }

    [Fact]
    public void Duplicate_name_among_registered_projects_is_rejected()
    {
        var result = Scaffolder().Validate("demo", null, Registry(("demo", @"C:\GIT\demo")));

        Assert.Equal(ProjectScaffolder.ValidationOutcome.NameNotUnique, result.Outcome);
    }

    [Fact]
    public void Name_that_sanitizes_to_empty_is_rejected()
    {
        var result = Scaffolder().Validate(":::", null, Registry());

        Assert.Equal(ProjectScaffolder.ValidationOutcome.DirNameEmpty, result.Outcome);
    }

    [Fact]
    public void Name_that_sanitizes_to_a_reserved_device_name_is_rejected()
    {
        var result = Scaffolder().Validate("CON", null, Registry());

        Assert.Equal(ProjectScaffolder.ValidationOutcome.DirNameReserved, result.Outcome);
    }

    [Fact]
    public void Existing_target_directory_is_rejected()
    {
        using var project = new TempProject();
        var config = new NewProjectConfig { Root = project.Root };
        Directory.CreateDirectory(Path.Combine(project.Root, "demo"));

        var result = Scaffolder(config).Validate("demo", null, Registry());

        Assert.Equal(ProjectScaffolder.ValidationOutcome.TargetExists, result.Outcome);
    }

    [Fact]
    public void Unknown_template_name_is_rejected()
    {
        using var project = new TempProject();
        var config = new NewProjectConfig { Root = project.Root, Templates = [] };

        var result = Scaffolder(config).Validate("demo", "does-not-exist", Registry());

        Assert.Equal(ProjectScaffolder.ValidationOutcome.TemplateNotFound, result.Outcome);
    }

    [Fact]
    public void A_fully_valid_request_computes_the_sanitized_dirname_and_target_path()
    {
        using var project = new TempProject();
        var config = new NewProjectConfig { Root = project.Root };

        var result = Scaffolder(config).Validate("My Cool Project", null, Registry());

        Assert.Equal(ProjectScaffolder.ValidationOutcome.Ok, result.Outcome);
        Assert.Equal("My-Cool-Project", result.DirName);
        Assert.Equal(Path.Combine(project.Root, "My-Cool-Project"), result.TargetPath);
    }
}

public class ProjectScaffolderScaffoldTests
{
    [Fact]
    public async Task Command_template_substitutes_target_and_name_placeholders()
    {
        using var project = new TempProject();
        var target = Path.Combine(project.Root, "demo");
        var runner = new FakeProcessRunner();
        var template = new NewProjectTemplate { Name = "gen", Type = "command", Command = "generator", Args = ["--target", "{target}", "--name", "{name}"] };
        var config = new NewProjectConfig { Root = project.Root, Templates = [template] };
        var scaffolder = new ProjectScaffolder(runner, new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        var result = await scaffolder.ScaffoldAsync("demo", "demo", target, "gen");

        Assert.True(result.Success);
        // The stub command doesn't actually create a .git dir, so ScaffoldAsync's own git-init
        // step also spawns — the template call is the first one either way.
        var call = runner.Calls[0];
        Assert.Equal("generator", call.Executable);
        Assert.Equal(["--target", target, "--name", "demo"], call.Arguments);
    }

    [Fact]
    public async Task Command_template_failure_exit_code_fails_the_scaffold_and_leaves_the_directory()
    {
        using var project = new TempProject();
        var target = Path.Combine(project.Root, "demo");
        var runner = new FakeProcessRunner { Results = new(new[] { new ProcessResult(3, "", "boom", false) }) };
        var template = new NewProjectTemplate { Name = "gen", Type = "command", Command = "generator" };
        var config = new NewProjectConfig { Root = project.Root, Templates = [template] };
        var scaffolder = new ProjectScaffolder(runner, new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        var result = await scaffolder.ScaffoldAsync("demo", "demo", target, "gen");

        Assert.False(result.Success);
        Assert.Contains("exited 3", result.Error);
        Assert.True(Directory.Exists(target), "a failed scaffold leaves the directory in place for diagnosis");
    }

    [Fact]
    public async Task Copy_template_copies_source_contents_and_skips_a_top_level_git_dir()
    {
        using var project = new TempProject();
        var source = Path.Combine(project.Root, "template-src");
        Directory.CreateDirectory(Path.Combine(source, ".git"));
        File.WriteAllText(Path.Combine(source, ".git", "HEAD"), "ref: refs/heads/main");
        Directory.CreateDirectory(Path.Combine(source, "src"));
        File.WriteAllText(Path.Combine(source, "src", "Program.cs"), "// hi");
        File.WriteAllText(Path.Combine(source, "README.md"), "hello");

        var target = Path.Combine(project.Root, "demo");
        var template = new NewProjectTemplate { Name = "copy", Type = "copy", Source = source };
        var config = new NewProjectConfig { Root = project.Root, Templates = [template] };
        var scaffolder = new ProjectScaffolder(new FakeProcessRunner(), new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        var result = await scaffolder.ScaffoldAsync("demo", "demo", target, "copy");

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(target, "README.md")));
        Assert.True(File.Exists(Path.Combine(target, "src", "Program.cs")));
        Assert.False(Directory.Exists(Path.Combine(target, ".git")), "the source's own .git must not be copied in");
    }

    [Fact]
    public async Task Missing_spec_kit_directory_scaffolds_successfully_with_a_warning()
    {
        using var project = new TempProject();
        var target = Path.Combine(project.Root, "demo");
        var config = new NewProjectConfig { Root = project.Root, SpecKit = Path.Combine(project.Root, "does-not-exist") };
        var scaffolder = new ProjectScaffolder(new FakeProcessRunner(), new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        var result = await scaffolder.ScaffoldAsync("demo", "demo", target, null);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("spec_kit"));
    }

    [Fact]
    public async Task No_spec_kit_configured_scaffolds_successfully_with_a_warning()
    {
        using var project = new TempProject();
        var target = Path.Combine(project.Root, "demo");
        var config = new NewProjectConfig { Root = project.Root };
        var scaffolder = new ProjectScaffolder(new FakeProcessRunner(), new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        var result = await scaffolder.ScaffoldAsync("demo", "demo", target, null);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("not configured"));
    }

    [Fact]
    public async Task Spec_kit_is_copied_into_docs_spec_kit()
    {
        using var project = new TempProject();
        var specKitSource = Path.Combine(project.Root, "spec-kit-src");
        Directory.CreateDirectory(specKitSource);
        File.WriteAllText(Path.Combine(specKitSource, "0001-adopt-spec-system.md"), "# adopt");

        var target = Path.Combine(project.Root, "demo");
        var config = new NewProjectConfig { Root = project.Root, SpecKit = specKitSource };
        var scaffolder = new ProjectScaffolder(new FakeProcessRunner(), new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        var result = await scaffolder.ScaffoldAsync("demo", "demo", target, null);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(target, "docs", "spec-kit", "0001-adopt-spec-system.md")));
    }

    [Fact]
    public async Task Gitignore_gets_only_the_missing_runtime_lines_when_the_template_already_wrote_one()
    {
        using var project = new TempProject();
        var source = Path.Combine(project.Root, "template-src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, ".gitignore"), "bin/\nobj/\n.magnaflow/*\n");

        var target = Path.Combine(project.Root, "demo");
        var template = new NewProjectTemplate { Name = "copy", Type = "copy", Source = source };
        var config = new NewProjectConfig { Root = project.Root, Templates = [template] };
        var scaffolder = new ProjectScaffolder(new FakeProcessRunner(), new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        await scaffolder.ScaffoldAsync("demo", "demo", target, "copy");

        var gitignore = await File.ReadAllTextAsync(Path.Combine(target, ".gitignore"));
        Assert.Contains("bin/", gitignore);
        Assert.Contains(".magnaflow/*", gitignore);
        Assert.Contains("!.magnaflow/config.yml", gitignore);
        // the pre-existing line was not duplicated
        Assert.Single(gitignore.Split('\n'), l => l.Trim() == ".magnaflow/*");
    }

    [Fact]
    public async Task Config_yml_stub_is_not_written_when_the_template_already_provided_one()
    {
        using var project = new TempProject();
        var source = Path.Combine(project.Root, "template-src");
        Directory.CreateDirectory(Path.Combine(source, ".magnaflow"));
        File.WriteAllText(Path.Combine(source, ".magnaflow", "config.yml"), "build:\n  command: echo hi\n");

        var target = Path.Combine(project.Root, "demo");
        var template = new NewProjectTemplate { Name = "copy", Type = "copy", Source = source };
        var config = new NewProjectConfig { Root = project.Root, Templates = [template] };
        var scaffolder = new ProjectScaffolder(new FakeProcessRunner(), new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        await scaffolder.ScaffoldAsync("demo", "demo", target, "copy");

        var content = await File.ReadAllTextAsync(Path.Combine(target, ".magnaflow", "config.yml"));
        Assert.Equal("build:\n  command: echo hi\n", content);
    }

    [Fact]
    public async Task Git_init_is_skipped_when_the_template_already_produced_a_git_directory()
    {
        // A "copy" template deliberately never leaves a .git behind (it always skips the source's
        // top-level .git) — the only way a template produces one is a "command" generator that
        // clones. Simulate that by having the stub command itself create .git as a side effect.
        using var project = new TempProject();
        var target = Path.Combine(project.Root, "demo");
        var runner = new FakeProcessRunner
        {
            OnRun = (_, _, _, _) => { Directory.CreateDirectory(Path.Combine(target, ".git")); return new ProcessResult(0, "cloned", "", false); },
        };
        var template = new NewProjectTemplate { Name = "gen", Type = "command", Command = "generator" };
        var config = new NewProjectConfig { Root = project.Root, Templates = [template] };
        var scaffolder = new ProjectScaffolder(runner, new FakeGitClient(), new DraftWriter(new FakeGitClient(), new FakeClock()), config);

        var result = await scaffolder.ScaffoldAsync("demo", "demo", target, "gen");

        Assert.True(result.Success);
        Assert.DoesNotContain(runner.Calls, c => c.Executable == "git" && c.Arguments.Contains("init"));
    }

    [Fact]
    public async Task Successful_scaffold_seeds_a_draft_and_commits_the_project()
    {
        using var project = new TempProject();
        var target = Path.Combine(project.Root, "demo");
        var git = new FakeGitClient();
        var config = new NewProjectConfig { Root = project.Root };
        var scaffolder = new ProjectScaffolder(new FakeProcessRunner(), git, new DraftWriter(git, new FakeClock()), config);

        var result = await scaffolder.ScaffoldAsync("demo", "demo", target, null);

        Assert.True(result.Success);
        Assert.NotNull(result.SeededDraft);
        Assert.Equal("0001-adopt-spec-system", result.SeededDraft!.Id);
        var draftContent = await File.ReadAllTextAsync(result.SeededDraft.FilePath);
        Assert.Contains("status: draft", draftContent);
        var commitAll = Assert.Single(git.CommitAllCalls);
        Assert.Equal("cockpit: create project demo", commitAll.Message);
        Assert.Single(git.Commits); // the draft's own write-#1 commit
    }
}
