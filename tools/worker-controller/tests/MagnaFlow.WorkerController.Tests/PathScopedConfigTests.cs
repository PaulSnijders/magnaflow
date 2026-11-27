using MagnaFlow.WorkerController.Config;

namespace MagnaFlow.WorkerController.Tests;

/// <summary>`commands:` entries as a string or a `{run, paths}` mapping (prompts/0037 decision 1).</summary>
public class PathScopedConfigTests : IDisposable
{
    private readonly TempProject _project = new();

    private (ProjectConfig? Config, string? Error) Load(string yaml)
    {
        _project.WriteFile(".magnaflow/config.yml", yaml);
        return ProjectConfig.Load(_project.Root);
    }

    [Fact]
    public void Load_ParsesAMixedListOfStringsAndMappings()
    {
        var (config, error) = Load(
            """
            build:
              commands:
                - node more/cmd/mf-klaas-backend.js build
                - run: npm --prefix topo run build
                  paths: [topo/]
            test:
              commands:
                - run: node more/cmd/mf-klaas-backend.js test
                  paths: [klaas-backend/, ./more/cmd/mf-klaas-backend.js]
            """);

        Assert.Null(error);
        Assert.Equal(
            [new CheckCommand("node more/cmd/mf-klaas-backend.js build"), new CheckCommand("npm --prefix topo run build", ["topo/"])],
            config!.BuildCommands, new CheckComparer());
        Assert.Equal(["klaas-backend/", "more/cmd/mf-klaas-backend.js"], config.TestCommands[0].Paths!);
        Assert.True(config.HasPathScopedCommands);
    }

    [Fact]
    public void Load_StringOnlyConfigHasNoPathScopedCommands()
    {
        var (config, error) = Load("build:\n  commands: [a, b]\ntest:\n  command: t\n");

        Assert.Null(error);
        Assert.False(config!.HasPathScopedCommands);
        Assert.All(config.BuildCommands, c => Assert.Null(c.Paths));
    }

    [Theory]
    [InlineData("- paths: [topo/]", "build.commands[1] needs a run: command")]
    [InlineData("- run: x\n    paths: []", "build.commands[1].paths must be a non-empty list")]
    [InlineData("- run: x", "build.commands[1].paths must be a non-empty list")]
    [InlineData("- run: x\n    paths: [/etc/]", "'/etc/' is absolute")]
    [InlineData("- run: x\n    paths: ['C:/repo/']", "'C:/repo/' is absolute")]
    [InlineData("- run: x\n    paths: [topo/../etc/]", "contains '..'")]
    [InlineData("- run: x\n    path: [topo/]", "unknown key(s) path")]
    [InlineData("- run: x\n    paths: topo/", "build.commands[1].paths must be a non-empty list")]
    public void Load_RejectsAnInvalidMappingEntry(string entry, string expected)
    {
        var (config, error) = Load($"build:\n  commands:\n  {entry}\n");

        Assert.Null(config);
        Assert.Contains("has an invalid value", error);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Load_NamesTheEntryByItsSectionAndPosition()
    {
        var (_, error) = Load("build:\n  command: b\ntest:\n  commands:\n    - t1\n    - run: t2\n      paths: []\n");

        Assert.Contains("test.commands[2].paths", error);
    }

    [Theory]
    [InlineData("topo/", "topo/src/a.ts", true)]
    [InlineData("topo/", "topology.md", false)]
    [InlineData("topo", "topo/src/a.ts", true)]
    [InlineData("topo", "topology.md", false)]
    [InlineData("more/cmd/x.js", "more/cmd/x.js", true)]
    [InlineData("more/cmd/x.js", "more/cmd/x.json", false)]
    public void Matches_TreatsATrailingSlashAsAFolderAndNeverMatchesANamePrefix(string entry, string touched, bool expected) =>
        Assert.Equal(expected, CheckCommand.Matches(entry, touched));

    private sealed class CheckComparer : IEqualityComparer<CheckCommand>
    {
        public bool Equals(CheckCommand? x, CheckCommand? y) =>
            x!.Run == y!.Run && (x.Paths ?? []).SequenceEqual(y.Paths ?? []);
        public int GetHashCode(CheckCommand obj) => obj.Run.GetHashCode();
    }

    public void Dispose() => _project.Dispose();
}
