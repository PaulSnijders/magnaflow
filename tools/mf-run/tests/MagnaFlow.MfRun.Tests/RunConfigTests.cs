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

    [Fact]
    public void ConfigWithoutStableBlockHasNoStable()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              services:
                - name: web
                  command: web.exe
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(error);
        Assert.Null(config!.Stable);
        Assert.Single(config.Services);
    }

    [Fact]
    public void ParsesStableBlock()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              stable:
                publish: Forecast/mf-publish-stable
                command: Forecast.exe
                args: ["--urls", "http://localhost:7300"]
                url: http://localhost:7300
                link: https://host.tailnet.ts.net/app/
                timeout_minutes: 20
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(error);
        Assert.Empty(config!.Services);
        var stable = config.Stable!;
        Assert.Equal("Forecast/mf-publish-stable", stable.Publish);
        Assert.Equal("Forecast.exe", stable.Command);
        Assert.Equal(["--urls", "http://localhost:7300"], stable.Args);
        Assert.Equal("http://localhost:7300", stable.Url);
        Assert.Equal("https://host.tailnet.ts.net/app/", stable.Link);
        Assert.Equal(TimeSpan.FromMinutes(20), stable.Timeout);
    }

    [Fact]
    public void StableDefaults()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              stable:
                publish: publish.sh
                command: App
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(error);
        var stable = config!.Stable!;
        Assert.Empty(stable.Args);
        Assert.Null(stable.Url);
        Assert.Null(stable.Link);
        Assert.Equal(TimeSpan.FromMinutes(15), stable.Timeout);
    }

    [Fact]
    public void StableServiceRunsFromCurrentFolder()
    {
        var stable = new StableConfig("publish", "App.exe", ["--x"], "http://localhost:7300", null, TimeSpan.FromMinutes(15));
        var root = Path.Combine(Path.GetTempPath(), "proj");

        var service = stable.ToService(root);

        var current = Path.Combine(root, ".magnaflow", "stable", "current");
        Assert.Equal("stable", service.Name);
        Assert.Equal(Path.Combine(current, "App.exe"), service.Command);
        Assert.Equal(current, service.Workdir);
        Assert.Equal(["--x"], service.Args);
        Assert.Equal("http://localhost:7300", service.Url);
    }

    [Theory]
    [InlineData("    command: App\n", "run.stable has no publish")]
    [InlineData("    publish: p.sh\n", "run.stable has no command")]
    [InlineData("    publish: p.sh\n    command: App\n    timeout_minutes: 0\n", "timeout_minutes must be at least 1")]
    public void InvalidStableBlockIsRejected(string body, string expected)
    {
        using var project = new TempProject();
        project.WriteConfig("run:\n  stable:\n" + body);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(config);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void ServiceNamedStableIsRejected()
    {
        using var project = new TempProject();
        project.WriteConfig("""
            run:
              services:
                - name: stable
                  command: web.exe
            """);

        var (config, error) = RunConfig.Load(project.Root);

        Assert.Null(config);
        Assert.Contains("reserved for run.stable", error);
    }
}
