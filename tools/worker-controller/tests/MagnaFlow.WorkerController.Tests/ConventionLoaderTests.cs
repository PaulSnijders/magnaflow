using MagnaFlow.WorkerController.Execution;

namespace MagnaFlow.WorkerController.Tests;

public class ConventionLoaderTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void Load_ReturnsEmptyWhenNeitherFileExists() =>
        Assert.Equal("", ConventionLoader.Load(_project.Root));

    [Fact]
    public void Load_IncludesClaudeMdWhenPresent()
    {
        _project.WriteFile("CLAUDE.md", "Always write tests.");

        Assert.Contains("Always write tests.", ConventionLoader.Load(_project.Root));
    }

    [Fact]
    public void Load_PrefersSpecifyMemoryConstitutionOverDocsConstitution()
    {
        _project.WriteFile(".specify/memory/constitution.md", "Constitution A.");
        _project.WriteFile("docs/constitution.md", "Constitution B.");

        var loaded = ConventionLoader.Load(_project.Root);

        Assert.Contains("Constitution A.", loaded);
        Assert.DoesNotContain("Constitution B.", loaded);
    }

    [Fact]
    public void Load_FallsBackToDocsConstitutionWhenSpecifyMemoryAbsent()
    {
        _project.WriteFile("docs/constitution.md", "Constitution B.");

        Assert.Contains("Constitution B.", ConventionLoader.Load(_project.Root));
    }

    [Fact]
    public void Load_IncludesBothClaudeMdAndConstitutionWhenBothExist()
    {
        _project.WriteFile("CLAUDE.md", "House rules.");
        _project.WriteFile(".specify/memory/constitution.md", "Core principles.");

        var loaded = ConventionLoader.Load(_project.Root);

        Assert.Contains("House rules.", loaded);
        Assert.Contains("Core principles.", loaded);
    }

    public void Dispose() => _project.Dispose();
}
