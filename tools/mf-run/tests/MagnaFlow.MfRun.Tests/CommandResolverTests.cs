using MagnaFlow.MfRun.Runtime;

namespace MagnaFlow.MfRun.Tests;

public class CommandResolverTests
{
    [Fact]
    public void ResolvesLiteralPathWithoutTryingFallbacks()
    {
        using var project = new TempProject();
        var path = project.WriteFile("web.exe", "stub");

        var result = CommandResolver.Resolve(path, isWindows: true);

        Assert.Equal(path, result.Path);
        Assert.False(result.ResolvedViaFallback);
        Assert.Equal([path], result.Attempted);
    }

    [Fact]
    public void CommandWithExtensionNeverFallsBackEvenIfAnotherExtensionExists()
    {
        using var project = new TempProject();
        project.WriteFile("web.exe", "stub"); // exists, but not what's asked for
        var missingCmd = Path.Combine(project.Root, "web.cmd");

        var result = CommandResolver.Resolve(missingCmd, isWindows: true);

        Assert.Null(result.Path);
        Assert.Equal([missingCmd], result.Attempted);
    }

    [Theory]
    [InlineData("web.cmd")]
    [InlineData("web.bat")]
    [InlineData("web.exe")]
    public void WindowsTriesExtensionsInOrderAndStopsAtFirstMatch(string presentFile)
    {
        using var project = new TempProject();
        var presentPath = project.WriteFile(presentFile, "stub");
        var extensionless = Path.Combine(project.Root, "web");

        var result = CommandResolver.Resolve(extensionless, isWindows: true);

        Assert.Equal(presentPath, result.Path);
        Assert.True(result.ResolvedViaFallback);
        Assert.Equal(extensionless, result.Attempted[0]);
        Assert.Equal(presentPath, result.Attempted[^1]);
    }

    [Fact]
    public void WindowsPrefersCmdOverBatAndExeWhenAllExist()
    {
        using var project = new TempProject();
        project.WriteFile("web.exe", "stub");
        project.WriteFile("web.bat", "stub");
        var cmdPath = project.WriteFile("web.cmd", "stub");
        var extensionless = Path.Combine(project.Root, "web");

        var result = CommandResolver.Resolve(extensionless, isWindows: true);

        Assert.Equal(cmdPath, result.Path);
    }

    [Fact]
    public void WindowsNotFoundListsEveryExtensionTried()
    {
        using var project = new TempProject();
        var extensionless = Path.Combine(project.Root, "web");

        var result = CommandResolver.Resolve(extensionless, isWindows: true);

        Assert.Null(result.Path);
        Assert.Equal(
            [extensionless, extensionless + ".cmd", extensionless + ".bat", extensionless + ".exe"],
            result.Attempted);
    }

    [Fact]
    public void UnixTriesShAfterTheBareLiteral()
    {
        using var project = new TempProject();
        var shPath = project.WriteFile("web.sh", "stub");
        var extensionless = Path.Combine(project.Root, "web");

        var result = CommandResolver.Resolve(extensionless, isWindows: false);

        Assert.Equal(shPath, result.Path);
        Assert.True(result.ResolvedViaFallback);
        Assert.Equal([extensionless, shPath], result.Attempted);
    }

    [Fact]
    public void UnixNotFoundListsBareLiteralAndSh()
    {
        using var project = new TempProject();
        var extensionless = Path.Combine(project.Root, "web");

        var result = CommandResolver.Resolve(extensionless, isWindows: false);

        Assert.Null(result.Path);
        Assert.Equal([extensionless, extensionless + ".sh"], result.Attempted);
    }

    [Fact]
    public void UnixResolvesBareLiteralExecutableWithoutTryingSh()
    {
        using var project = new TempProject();
        var path = project.WriteFile("web", "stub");

        var result = CommandResolver.Resolve(path, isWindows: false);

        Assert.Equal(path, result.Path);
        Assert.False(result.ResolvedViaFallback);
        Assert.Equal([path], result.Attempted);
    }
}
