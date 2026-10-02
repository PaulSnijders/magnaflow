using MagnaFlow.MfCockpit.Config;

namespace MagnaFlow.MfCockpit.Tests;

public class ProjectConfigReaderTests
{
    private const string FullConfig = """
        build:
          command: dotnet build
        test:
          commands:
            - dotnet test
            - dotnet format --verify-no-changes
        defaults:
          max_attempts: 5
        run:
          services:
            - name: web
              command: bin/web.exe
            - name: api
              command: bin/api.exe
        """;

    [Fact]
    public void Summarize_ParsesBuildTestMaxAttemptsAndRunServices()
    {
        var summary = ProjectConfigReader.Summarize(FullConfig);

        Assert.Equal(["dotnet build"], summary.BuildCommands);
        Assert.Equal(["dotnet test", "dotnet format --verify-no-changes"], summary.TestCommands);
        Assert.Equal(5, summary.MaxAttempts);
        Assert.Equal(["web", "api"], summary.RunServices);
    }

    [Fact]
    public void Summarize_DefaultsMaxAttemptsToThreeWhenAbsent()
    {
        var summary = ProjectConfigReader.Summarize("build:\n  command: dotnet build\n");
        Assert.Equal(3, summary.MaxAttempts);
        Assert.Empty(summary.RunServices);
    }

    [Fact]
    public void Summarize_ToleratesBrokenYaml_ReturnsEmptyDefaults()
    {
        var summary = ProjectConfigReader.Summarize("build: [this is not valid");
        Assert.Empty(summary.BuildCommands);
        Assert.Empty(summary.TestCommands);
        Assert.Equal(3, summary.MaxAttempts);
        Assert.Empty(summary.RunServices);
    }

    [Fact]
    public void ReadRunServiceNames_NullWhenNoRunBlockAtAll()
    {
        Assert.Null(ProjectConfigReader.ReadRunServiceNames("build:\n  command: dotnet build\n"));
    }

    [Fact]
    public void ReadRunServiceNames_NullWhenContentFailsToParse()
    {
        Assert.Null(ProjectConfigReader.ReadRunServiceNames("run: [this is not valid"));
    }

    [Fact]
    public void ReadRunServiceNames_EmptyListWhenRunBlockHasNoServicesKey()
    {
        var names = ProjectConfigReader.ReadRunServiceNames("run:\n  command: mf-run\n");
        Assert.NotNull(names);
        Assert.Empty(names);
    }

    [Fact]
    public void ReadRunServiceNames_ListsConfiguredNames()
    {
        var names = ProjectConfigReader.ReadRunServiceNames(FullConfig);
        Assert.Equal(["web", "api"], names);
    }

    [Fact]
    public void Validate_ValidContentHasNoWarningsOrError()
    {
        var (warnings, error) = ProjectConfigReader.Validate(FullConfig);
        Assert.Empty(warnings);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_UnknownTopLevelKeyIsAWarningNotARejection()
    {
        var (warnings, error) = ProjectConfigReader.Validate("build:\n  command: x\nsome_future_tool:\n  flag: true\n");
        Assert.Null(error);
        Assert.Single(warnings);
        Assert.Contains("some_future_tool", warnings[0]);
    }

    [Fact]
    public void Validate_BrokenYamlReturnsParseErrorWithPositionInfo()
    {
        var (warnings, error) = ProjectConfigReader.Validate("build: [this is not valid");
        Assert.Empty(warnings);
        Assert.NotNull(error);
        Assert.Contains("line", error);
        Assert.Contains("column", error);
    }

    [Fact]
    public void ComputeHash_IsStableAndChangesWithContent()
    {
        var h1 = ProjectConfigReader.ComputeHash("a: 1\n");
        var h2 = ProjectConfigReader.ComputeHash("a: 1\n");
        var h3 = ProjectConfigReader.ComputeHash("a: 2\n");

        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);
    }
}
