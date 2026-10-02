using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Tests;

public class PathGuardTests
{
    [Fact]
    public void ResolveInside_returns_root_for_null_or_empty()
    {
        using var project = new TempProject();
        Assert.Equal(Path.GetFullPath(project.Root), PathGuard.ResolveInside(project.Root, null));
        Assert.Equal(Path.GetFullPath(project.Root), PathGuard.ResolveInside(project.Root, ""));
    }

    [Fact]
    public void ResolveInside_allows_nested_relative_path()
    {
        using var project = new TempProject();
        var resolved = PathGuard.ResolveInside(project.Root, "sub/dir/file.md");
        Assert.NotNull(resolved);
        Assert.StartsWith(Path.GetFullPath(project.Root), resolved);
    }

    [Fact]
    public void ResolveInside_rejects_dotdot_escape()
    {
        using var project = new TempProject();
        Assert.Null(PathGuard.ResolveInside(project.Root, "../../outside.txt"));
    }

    [Fact]
    public void ResolveInside_rejects_rooted_absolute_path()
    {
        using var project = new TempProject();
        Assert.Null(PathGuard.ResolveInside(project.Root, @"C:\Windows\win.ini"));
    }

    [Fact]
    public void ResolveInside_rejects_path_containing_drive_colon()
    {
        using var project = new TempProject();
        Assert.Null(PathGuard.ResolveInside(project.Root, "sub/C:/evil"));
    }

    [Fact]
    public void ResolveInside_allows_dotdot_that_stays_inside_root()
    {
        using var project = new TempProject();
        var resolved = PathGuard.ResolveInside(project.Root, "sub/../ok.txt");
        Assert.NotNull(resolved);
        Assert.Equal(Path.GetFullPath(Path.Combine(project.Root, "ok.txt")), resolved);
    }
}
