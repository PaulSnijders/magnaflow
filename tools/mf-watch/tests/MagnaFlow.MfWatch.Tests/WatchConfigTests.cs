using MagnaFlow.MfWatch.Config;

namespace MagnaFlow.MfWatch.Tests;

public class WatchConfigTests
{
    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var (config, error, notice) = WatchConfig.Load(Path.Combine(Path.GetTempPath(), "mf-watch-tests", Guid.NewGuid().ToString("N") + ".yml"));

        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
        Assert.False(config!.GitSync);
        Assert.Equal("mf-worker", config.WorkerCommand);
        Assert.Empty(config.WorkerArgs);
        Assert.Null(config.NotifyCommand);
        Assert.Equal(TimeSpan.FromMinutes(1), config.IntervalMin);
        Assert.Equal(TimeSpan.FromMinutes(15), config.IntervalMax);
        Assert.Equal(TimeSpan.FromMinutes(30), config.IdleGrace);
    }

    [Fact]
    public void ParsesAllFieldsFromLegacyFlatFormat()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-watch.yml", """
            git_sync: true
            worker:
              command: /path/to/mf-worker
              args: [--verbose]
            notify_command: notify-send "{title}" "{message}"
            interval_min_minutes: 2
            interval_max_minutes: 20
            idle_grace_minutes: 45
            worker_timeout_minutes: 30
            """);

        var (config, error, notice) = WatchConfig.Load(path);

        Assert.Null(error);
        Assert.NotNull(notice);
        Assert.NotNull(config);
        Assert.True(config!.GitSync);
        Assert.Equal("/path/to/mf-worker", config.WorkerCommand);
        Assert.Equal(["--verbose"], config.WorkerArgs);
        Assert.Equal("notify-send \"{title}\" \"{message}\"", config.NotifyCommand);
        Assert.Equal(TimeSpan.FromMinutes(2), config.IntervalMin);
        Assert.Equal(TimeSpan.FromMinutes(20), config.IntervalMax);
        Assert.Equal(TimeSpan.FromMinutes(45), config.IdleGrace);
        Assert.Equal(TimeSpan.FromMinutes(30), config.WorkerTimeout);
    }

    [Fact]
    public void ParsesAllFieldsFromSectionedFormat()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", """
            watch:
              git_sync: true
              worker:
                command: /path/to/mf-worker
                args: [--verbose]
              notify_command: notify-send "{title}" "{message}"
              interval_min_minutes: 2
              interval_max_minutes: 20
              idle_grace_minutes: 45
              worker_timeout_minutes: 30
            """);

        var (config, error, notice) = WatchConfig.Load(path);

        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
        Assert.True(config!.GitSync);
        Assert.Equal("/path/to/mf-worker", config.WorkerCommand);
        Assert.Equal(["--verbose"], config.WorkerArgs);
        Assert.Equal("notify-send \"{title}\" \"{message}\"", config.NotifyCommand);
        Assert.Equal(TimeSpan.FromMinutes(2), config.IntervalMin);
        Assert.Equal(TimeSpan.FromMinutes(20), config.IntervalMax);
        Assert.Equal(TimeSpan.FromMinutes(45), config.IdleGrace);
        Assert.Equal(TimeSpan.FromMinutes(30), config.WorkerTimeout);
    }

    [Fact]
    public void IgnoresTheOtherTools_CockpitSection()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", """
            cockpit:
              port: 9999
              bind: 0.0.0.0
            """);

        var (config, error, notice) = WatchConfig.Load(path);

        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
        Assert.False(config!.GitSync);
        Assert.Equal("mf-worker", config.WorkerCommand);
    }

    [Fact]
    public void SectionedFormatWinsOverStrayTopLevelFields()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", """
            watch:
              git_sync: true
            cockpit:
              port: 9999
            """);

        var (config, error, notice) = WatchConfig.Load(path);

        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
        Assert.True(config!.GitSync);
    }

    [Fact]
    public void LegacyFileNameForcesLegacyParsingAndAlwaysNotices()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-watch.yml", "git_sync: true");

        var (config, error, notice) = WatchConfig.Load(path, isLegacyFileName: true);

        Assert.Null(error);
        Assert.NotNull(notice);
        Assert.Contains("magnaflow.yml", notice);
        Assert.NotNull(config);
        Assert.True(config!.GitSync);
    }

    [Fact]
    public void LegacyFileNameNoticesEvenWithoutAnyLegacyFields()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-watch.yml", "");

        var (config, error, notice) = WatchConfig.Load(path, isLegacyFileName: true);

        Assert.Null(error);
        Assert.NotNull(notice);
        Assert.NotNull(config);
    }

    [Fact]
    public void InvalidYamlIsAnError()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-watch.yml", "git_sync: [this is not a bool");

        var (config, error, notice) = WatchConfig.Load(path);

        Assert.Null(config);
        Assert.NotNull(error);
    }

    [Fact]
    public void IntervalMaxBelowMinIsAnError()
    {
        using var project = new TempProject();
        var path = project.WriteFile("mf-watch.yml", """
            interval_min_minutes: 10
            interval_max_minutes: 5
            """);

        var (config, error, notice) = WatchConfig.Load(path);

        Assert.Null(config);
        Assert.Contains("interval_max_minutes", error);
    }

    [Fact]
    public void IntervalMaxBelowMinIsAnErrorInSectionedFormat()
    {
        using var project = new TempProject();
        var path = project.WriteFile("magnaflow.yml", """
            watch:
              interval_min_minutes: 10
              interval_max_minutes: 5
            """);

        var (config, error, notice) = WatchConfig.Load(path);

        Assert.Null(config);
        Assert.Contains("interval_max_minutes", error);
    }
}

public class WatchConfigLocatePathTests
{
    [Fact]
    public void ExplicitConfigArgWinsAndIsNeverTreatedAsLegacy()
    {
        using var project = new TempProject();
        var explicitPath = Path.Combine(project.Root, "somewhere", "custom.yml");

        var (path, isLegacy) = WatchConfig.LocatePath(explicitPath, project.Root, project.Root);

        Assert.Equal(Path.GetFullPath(explicitPath), path);
        Assert.False(isLegacy);
    }

    [Fact]
    public void ExplicitConfigArgDoesNotFallThroughWhenMissing()
    {
        using var project = new TempProject();
        var explicitPath = Path.Combine(project.Root, "missing.yml");
        project.WriteFile("magnaflow.yml", "watch:\n  git_sync: true\n");

        var (path, isLegacy) = WatchConfig.LocatePath(explicitPath, project.Root, project.Root);

        Assert.Equal(Path.GetFullPath(explicitPath), path);
        Assert.False(isLegacy);

        var (config, error, notice) = WatchConfig.Load(path, isLegacy);
        Assert.Null(error);
        Assert.Null(notice);
        Assert.False(config!.GitSync); // the sibling magnaflow.yml is NOT consulted
    }

    [Fact]
    public void MagnaflowYmlNextToBinaryWinsOverUserDirAndLegacy()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();
        baseDir.WriteFile("magnaflow.yml", "watch:\n  git_sync: true\n");
        userDir.WriteFile("magnaflow.yml", "watch:\n  git_sync: false\n");
        baseDir.WriteFile("mf-watch.yml", "git_sync: false\n");

        var (path, isLegacy) = WatchConfig.LocatePath(null, baseDir.Root, userDir.Root);

        Assert.Equal(Path.Combine(baseDir.Root, "magnaflow.yml"), path);
        Assert.False(isLegacy);
    }

    [Fact]
    public void UserConfigDirWinsWhenNoMagnaflowYmlNextToBinary()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();
        userDir.WriteFile("magnaflow.yml", "watch:\n  git_sync: true\n");
        baseDir.WriteFile("mf-watch.yml", "git_sync: false\n");

        var (path, isLegacy) = WatchConfig.LocatePath(null, baseDir.Root, userDir.Root);

        Assert.Equal(Path.Combine(userDir.Root, "magnaflow.yml"), path);
        Assert.False(isLegacy);
    }

    [Fact]
    public void LegacyFileNameIsTheLastResort()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();
        baseDir.WriteFile("mf-watch.yml", "git_sync: true\n");

        var (path, isLegacy) = WatchConfig.LocatePath(null, baseDir.Root, userDir.Root);

        Assert.Equal(Path.Combine(baseDir.Root, "mf-watch.yml"), path);
        Assert.True(isLegacy);
    }

    [Fact]
    public void NothingFoundAnywhereYieldsMissingFileDefaults()
    {
        using var baseDir = new TempProject();
        using var userDir = new TempProject();

        var (path, isLegacy) = WatchConfig.LocatePath(null, baseDir.Root, userDir.Root);
        Assert.False(isLegacy);

        var (config, error, notice) = WatchConfig.Load(path, isLegacy);
        Assert.Null(error);
        Assert.Null(notice);
        Assert.NotNull(config);
    }
}
