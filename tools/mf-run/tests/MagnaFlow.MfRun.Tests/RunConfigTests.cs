using MagnaFlow.MfRun.Config;

namespace MagnaFlow.MfRun.Tests;

public class RunConfigTests
{
    [Fact]
    public void MissingFileYieldsNoServices()
    {
        var (config, error) = RunConfig.Load(Path.Combine(Path.GetTempPath(), "mf-run-tests", Guid.NewGuid().ToString("N")));

        Assert.Null(error);
        Assert.NotNull(config);
        Assert.Empty(config!.Services);
    }

    [Fact]
    public void FileWithNoRunBlockYieldsNoServices()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            build:
              command: dotnet build
            test:
              command: dotnet test
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(error);
        Assert.Empty(config!.Services);
    }

    [Fact]
    public void ParsesAllFields()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              services:
                - name: web
                  command: src/MyApp/bin/Debug/net10.0/MyApp.exe
                  args: ["--urls", "http://0.0.0.0:5000"]
                  workdir: src/MyApp
                  url: http://worker-1:5000
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(error);
        var service = Assert.Single(config!.Services);
        Assert.Equal("web", service.Name);
        Assert.Equal("src/MyApp/bin/Debug/net10.0/MyApp.exe", service.Command);
        Assert.Equal(["--urls", "http://0.0.0.0:5000"], service.Args);
        Assert.Equal("src/MyApp", service.Workdir);
        Assert.Equal("http://worker-1:5000", service.Url);
    }

    [Fact]
    public void OptionalFieldsDefaultToNullOrEmpty()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              services:
                - name: web
                  command: MyApp.exe
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(error);
        var service = Assert.Single(config!.Services);
        Assert.Empty(service.Args);
        Assert.Null(service.Workdir);
        Assert.Null(service.Url);
    }

    [Fact]
    public void PreservesListOrder()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              services:
                - name: api
                  command: api.exe
                - name: web
                  command: web.exe
            """);

        var (config, _) = RunConfig.Load(project.Root);

        Assert.Equal(["api", "web"], config!.Services.Select(s => s.Name));
    }

    [Fact]
    public void DuplicateServiceNamesIsAnError()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              services:
                - name: web
                  command: web.exe
                - name: web
                  command: web2.exe
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(config);
        Assert.Contains("more than one service named 'web'", error);
    }

    [Fact]
    public void MissingCommandIsAnError()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              services:
                - name: web
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(config);
        Assert.Contains("no command", error);
    }

    [Fact]
    public void MissingNameIsAnError()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              services:
                - command: web.exe
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(config);
        Assert.Contains("no name", error);
    }

    [Fact]
    public void InvalidYamlIsAnError()
    {
        using var project = new TempProject();
        project.WriteConfig("run: [this is not valid: yaml structure");

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(config);
        Assert.NotNull(error);
    }

    [Fact]
    public void UnrelatedTopLevelSectionsAreIgnored()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            build:
              command: dotnet build
            agent:
              command: claude
            run:
              services:
                - name: web
                  command: web.exe
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(error);
        Assert.Single(config!.Services);
    }
}
