using MagnaFlow.WorkerController.Config;

namespace MagnaFlow.WorkerController.Tests;

public class ProjectConfigTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void Load_ReadsFullConfig()
    {
        _project.WriteFile(".magnaflow/config.yml",
            """
            build:
              command: dotnet build src
            test:
              command: dotnet test tests
            defaults:
              max_attempts: 5
              command_timeout_minutes: 10
            agent:
              command: claude
              args: [--dangerously-skip-permissions]
            """);

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(error);
        Assert.NotNull(config);
        Assert.Equal(["dotnet build src"], config.BuildCommands);
        Assert.Equal(["dotnet test tests"], config.TestCommands);
        Assert.Equal(5, config.MaxAttempts);
        Assert.Equal(TimeSpan.FromMinutes(10), config.CommandTimeout);
        Assert.Equal("claude", config.AgentCommand);
        Assert.Equal(["--dangerously-skip-permissions"], config.AgentArgs);
    }

    [Fact]
    public void Load_AppliesDefaultsForOptionalSections()
    {
        _project.WriteFile(".magnaflow/config.yml",
            """
            build:
              command: make
            test:
              command: make test
            """);

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(error);
        Assert.Equal(3, config!.MaxAttempts);
        Assert.Equal(TimeSpan.FromMinutes(30), config.CommandTimeout);
        Assert.Equal("claude", config.AgentCommand);
        Assert.Empty(config.AgentArgs);
        Assert.Equal("mf-run", config.RunCommand);
        Assert.False(config.HasRunServices);
    }

    [Fact]
    public void Load_DetectsRunServicesAndCustomRunCommand()
    {
        _project.WriteFile(".magnaflow/config.yml",
            """
            build:
              command: make
            test:
              command: make test
            run:
              command: /path/to/mf-run
              services:
                - name: web
                  command: bin/web.exe
            """);

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(error);
        Assert.True(config!.HasRunServices);
        Assert.Equal("/path/to/mf-run", config.RunCommand);
    }

    [Fact]
    public void Load_DetectsRunStable()
    {
        _project.WriteFile(".magnaflow/config.yml",
            """
            build:
              command: make
            test:
              command: make test
            run:
              stable:
                publish: publish
                command: App.exe
            """);

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(error);
        Assert.True(config!.HasRunStable);
        Assert.False(config.HasRunServices);
    }

    [Fact]
    public void Load_NoRunStableByDefault()
    {
        _project.WriteFile(".magnaflow/config.yml", "build:\n  command: make\ntest:\n  command: make test\nrun:\n  services:\n    - name: web\n      command: w.exe\n");

        var (config, _) = ProjectConfig.Load(_project.Root);

        Assert.False(config!.HasRunStable);
    }

    [Fact]
    public void Load_EmptyRunServicesListMeansNoRunServices()
    {
        _project.WriteFile(".magnaflow/config.yml",
            """
            build:
              command: make
            test:
              command: make test
            run:
              services: []
            """);

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(error);
        Assert.False(config!.HasRunServices);
    }

    [Fact]
    public void Load_ListsMissingRequiredFields()
    {
        _project.WriteFile(".magnaflow/config.yml", "defaults:\n  max_attempts: 2\n");

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(config);
        Assert.Contains("build.command", error);
        Assert.Contains("test.command", error);
    }

    [Fact]
    public void Load_FailsWhenFileAbsent()
    {
        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(config);
        Assert.Contains("no project configuration", error);
    }

    [Fact]
    public void Load_ParsesCommandsListForBuildAndTest()
    {
        _project.WriteFile(".magnaflow/config.yml",
            """
            build:
              commands:
                - dotnet build web/Wozzol.sln
                - npm --prefix wozzol-ionic run build
            test:
              commands:
                - dotnet test web/Wozzol.Tests/Wozzol.Tests.csproj
            """);

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(error);
        Assert.Equal(["dotnet build web/Wozzol.sln", "npm --prefix wozzol-ionic run build"], config!.BuildCommands);
        Assert.Equal(["dotnet test web/Wozzol.Tests/Wozzol.Tests.csproj"], config.TestCommands);
    }

    [Fact]
    public void Load_RejectsBothCommandAndCommandsForTheSameSection()
    {
        _project.WriteFile(".magnaflow/config.yml",
            """
            build:
              command: dotnet build
              commands:
                - dotnet build
            test:
              command: dotnet test
            """);

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(config);
        Assert.Contains("build.command and build.commands are both set", error);
    }

    [Fact]
    public void Load_RejectsEmptyCommandsList()
    {
        _project.WriteFile(".magnaflow/config.yml",
            """
            build:
              commands: []
            test:
              command: dotnet test
            """);

        var (config, error) = ProjectConfig.Load(_project.Root);

        Assert.Null(config);
        Assert.Contains("build.commands is empty", error);
    }

    public void Dispose() => _project.Dispose();
}
