using MagnaFlow.MfCockpit.Config;

namespace MagnaFlow.MfCockpit.Tests;

public class CockpitConfigTests
{
    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var (config, error, notice) = CockpitConfig.Load(Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + ".yml"));

        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
        Assert.Equal(5210, config!.Port);
        Assert.Equal("localhost", config.Bind);
        Assert.Empty(config.Projects);
        Assert.True(config.Chat.Enabled);
        Assert.Equal("claude", config.Chat.Command);
    }

    [Fact]
    public void ParsesAllFieldsFromLegacyFlatFormat()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-cockpit.yml", """
            port: 6000
            bind: 0.0.0.0
            projects:
              - name: demo
                path: C:/demo
            chat:
              enabled: false
              command: /path/to/claude
              args: [--flag]
              timeout_minutes: 10
            """);

        var (config, error, notice) = CockpitConfig.Load(path);

        Assert.Null(error);
        Assert.NotNull(notice);
        Assert.NotNull(config);
        Assert.Equal(6000, config!.Port);
        Assert.Equal("0.0.0.0", config.Bind);
        Assert.Single(config.Projects);
        Assert.Equal("demo", config.Projects[0].Name);
        Assert.False(config.Chat.Enabled);
        Assert.Equal("/path/to/claude", config.Chat.Command);
        Assert.Equal(["--flag"], config.Chat.Args);
        Assert.Equal(TimeSpan.FromMinutes(10), config.Chat.Timeout);
    }

    [Fact]
    public void ParsesAllFieldsFromSectionedFormat()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", """
            cockpit:
              port: 6000
              bind: 0.0.0.0
              projects:
                - name: demo
                  path: C:/demo
              chat:
                enabled: false
                command: /path/to/claude
                args: [--flag]
                timeout_minutes: 10
            """);

        var (config, error, notice) = CockpitConfig.Load(path);

        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
        Assert.Equal(6000, config!.Port);
        Assert.Equal("0.0.0.0", config.Bind);
        Assert.Single(config.Projects);
        Assert.False(config.Chat.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(10), config.Chat.Timeout);
    }

    [Fact]
    public void IgnoresTheOtherTools_WatchSection()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", """
            watch:
              git_sync: true
              worker_timeout_minutes: 30
            """);

        var (config, error, notice) = CockpitConfig.Load(path);

        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
        Assert.Equal(5210, config!.Port);
        Assert.Equal("localhost", config.Bind);
        Assert.Empty(config.Projects);
    }

    [Fact]
    public void SectionedFormatWinsOverStrayTopLevelFields()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", """
            cockpit:
              port: 6000
            watch:
              git_sync: true
            """);

        var (config, error, notice) = CockpitConfig.Load(path);

        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
        Assert.Equal(6000, config!.Port);
    }

    [Fact]
    public void LegacyFileNameForcesLegacyParsingAndAlwaysNotices()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-cockpit.yml", "port: 6000");

        var (config, error, notice) = CockpitConfig.Load(path, isLegacyFileName: true);

        Assert.Null(error);
        Assert.NotNull(notice);
        Assert.Contains("magnaflow.yml", notice);
        Assert.NotNull(config);
        Assert.Equal(6000, config!.Port);
    }

    [Fact]
    public void LegacyFileNameNoticesEvenWithoutAnyLegacyFields()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-cockpit.yml", "");

        var (config, error, notice) = CockpitConfig.Load(path, isLegacyFileName: true);

        Assert.Null(error);
        Assert.NotNull(notice);
        Assert.NotNull(config);
    }

    [Fact]
    public void InvalidYamlIsAnError()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-cockpit.yml", "port: [this is not a number");

        var (config, error, notice) = CockpitConfig.Load(path);

        Assert.Null(config);
        Assert.NotNull(error);
    }

    [Fact]
    public void DuplicateProjectNamesIsAnError()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-cockpit.yml", """
            projects:
              - name: demo
                path: C:/a
              - name: demo
                path: C:/b
            """);

        var (config, error, notice) = CockpitConfig.Load(path);

        Assert.Null(config);
        Assert.Contains("unique", error);
    }

    [Fact]
    public void DuplicateProjectNamesIsAnErrorInSectionedFormat()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", """
            cockpit:
              projects:
                - name: demo
                  path: C:/a
                - name: demo
                  path: C:/b
            """);

        var (config, error, notice) = CockpitConfig.Load(path);

        Assert.Null(config);
        Assert.Contains("unique", error);
    }
}

/// <summary>CockpitFactory (used by the API integration tests) sets MF_COCKPIT_CONFIG on the
/// process and never unsets it, so these tests — which assert on LocatePath's env-var-free
/// behavior — must not trust ambient process state left over from a prior test.</summary>
public class CockpitConfigLocatePathTests : IDisposable
{
    public CockpitConfigLocatePathTests() => Environment.SetEnvironmentVariable("MF_COCKPIT_CONFIG", null);
    public void Dispose() => Environment.SetEnvironmentVariable("MF_COCKPIT_CONFIG", null);

    [Fact]
    public void ExplicitConfigArgWinsAndIsNeverTreatedAsLegacy()
    {
        using var project = new TempProject();
        var explicitPath = Path.Combine(project.Root, "somewhere", "custom.yml");

        var (path, isLegacy) = CockpitConfig.LocatePath(["--config", explicitPath], project.Root, project.Root);

        Assert.Equal(Path.GetFullPath(explicitPath), path);
        Assert.False(isLegacy);
    }

    [Fact]
    public void ExplicitConfigArgDoesNotFallThroughWhenMissing()
    {
        using var project = new TempProject();
        var explicitPath = Path.Combine(project.Root, "missing.yml");
        project.WriteFile("magnaflow.yml", "cockpit:\n  port: 6000\n");

        var (path, isLegacy) = CockpitConfig.LocatePath(["--config", explicitPath], project.Root, project.Root);

        Assert.Equal(Path.GetFullPath(explicitPath), path);
        Assert.False(isLegacy);

        var (config, error, notice) = CockpitConfig.Load(path, isLegacy);
        Assert.Null(error);
        Assert.Null(notice);
        Assert.Equal(5210, config!.Port); // the sibling magnaflow.yml is NOT consulted
    }

    [Fact]
    public void MagnaflowYmlNextToBinaryWinsOverUserDirAndLegacy()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();
        baseDir.WriteFile("magnaflow.yml", "cockpit:\n  port: 1111\n");
        userDir.WriteFile("magnaflow.yml", "cockpit:\n  port: 2222\n");
        baseDir.WriteFile("mf-cockpit.yml", "port: 3333\n");

        var (path, isLegacy) = CockpitConfig.LocatePath([], baseDir.Root, userDir.Root);

        Assert.Equal(Path.Combine(baseDir.Root, "magnaflow.yml"), path);
        Assert.False(isLegacy);
    }

    [Fact]
    public void UserConfigDirWinsWhenNoMagnaflowYmlNextToBinary()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();
        userDir.WriteFile("magnaflow.yml", "cockpit:\n  port: 2222\n");
        baseDir.WriteFile("mf-cockpit.yml", "port: 3333\n");

        var (path, isLegacy) = CockpitConfig.LocatePath([], baseDir.Root, userDir.Root);

        Assert.Equal(Path.Combine(userDir.Root, "magnaflow.yml"), path);
        Assert.False(isLegacy);
    }

    [Fact]
    public void LegacyFileNameIsTheLastResort()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();
        baseDir.WriteFile("mf-cockpit.yml", "port: 3333\n");

        var (path, isLegacy) = CockpitConfig.LocatePath([], baseDir.Root, userDir.Root);

        Assert.Equal(Path.Combine(baseDir.Root, "mf-cockpit.yml"), path);
        Assert.True(isLegacy);
    }

    [Fact]
    public void NothingFoundAnywhereYieldsMissingFileDefaults()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();

        var (path, isLegacy) = CockpitConfig.LocatePath([], baseDir.Root, userDir.Root);
        Assert.False(isLegacy);

        var (config, error, notice) = CockpitConfig.Load(path, isLegacy);
        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
    }

    [Fact]
    public void EnvVarIsUsedWhenNoExplicitConfigArg()
    {
        using var project = new TempProject();
        var envPath = project.WriteFile("via-env.yml", "cockpit:\n  port: 7777\n");
        Environment.SetEnvironmentVariable("MF_COCKPIT_CONFIG", envPath);
        try
        {
            var (path, isLegacy) = CockpitConfig.LocatePath([], project.Root, project.Root);

            Assert.Equal(Path.GetFullPath(envPath), path);
            Assert.False(isLegacy);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MF_COCKPIT_CONFIG", null);
        }
    }
}
