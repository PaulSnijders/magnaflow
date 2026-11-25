using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Projects;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>The Add project → Existing folder picker (docs/specs/cockpit/index.md "Browse") on a
/// real temp tree.</summary>
public class DirectoryBrowserTests : IDisposable
{
    private readonly string _tree;
    private readonly string _home;

    public DirectoryBrowserTests()
    {
        _tree = Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", "browse-" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(_tree, "home");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tree, recursive: true); } catch { /* best effort */ }
    }

    private string Dir(params string[] parts)
    {
        var dir = Path.Combine([_tree, .. parts]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private DirectoryBrowser Browser(string? root = null) => new(new NewProjectConfig { Root = root }, _home);

    [Fact]
    public void Lists_subfolders_sorted_case_insensitively()
    {
        var folder = Dir("list");
        Dir("list", "b");
        Dir("list", "A");
        Dir("list", "c");

        var result = Browser().List(folder);

        Assert.Equal(DirectoryBrowser.ErrorKind.None, result.Error);
        Assert.Equal(["A", "b", "c"], result.Listing!.Dirs.Select(d => d.Name));
        Assert.Equal(Path.Combine(folder, "A"), result.Listing.Dirs[0].Path);
        Assert.Equal(folder, result.Listing.Path);
        Assert.Equal(_tree, result.Listing.Parent);
        Assert.NotEmpty(result.Listing.Roots);
    }

    [Fact]
    public void Leaves_out_dot_folders_and_files()
    {
        var folder = Dir("hidden");
        Dir("hidden", ".hidden");
        Dir("hidden", "visible");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "x");

        var names = Browser().List(folder).Listing!.Dirs.Select(d => d.Name).ToList();

        Assert.Equal(["visible"], names);
    }

    [Fact]
    public void Marks_a_git_directory_and_a_git_file_as_repos()
    {
        var folder = Dir("git");
        Dir("git", "repo", ".git");
        Dir("git", "worktree");
        File.WriteAllText(Path.Combine(folder, "worktree", ".git"), "gitdir: /elsewhere");
        Dir("git", "plain");

        var dirs = Browser().List(folder).Listing!.Dirs.ToDictionary(d => d.Name, d => d.Git);

        Assert.True(dirs["repo"]);
        Assert.True(dirs["worktree"]);
        Assert.False(dirs["plain"]);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("")]
    public void A_relative_or_empty_path_is_bad(string path)
    {
        Assert.Equal(DirectoryBrowser.ErrorKind.BadPath, Browser().List(path).Error);
    }

    [Fact]
    public void A_missing_directory_is_not_found()
    {
        Assert.Equal(DirectoryBrowser.ErrorKind.NotFound, Browser().List(Path.Combine(_tree, "nope")).Error);
    }

    [Fact]
    public void A_file_is_not_found()
    {
        var file = Path.Combine(Dir("f"), "a.txt");
        File.WriteAllText(file, "x");
        Assert.Equal(DirectoryBrowser.ErrorKind.NotFound, Browser().List(file).Error);
    }

    [Fact]
    public void Parent_is_null_at_a_root()
    {
        var root = Path.GetPathRoot(_tree)!;

        var result = Browser().List(root);

        Assert.Equal(DirectoryBrowser.ErrorKind.None, result.Error);
        Assert.Null(result.Listing!.Parent);
    }

    [Fact]
    public void StartPath_prefers_an_existing_absolute_hint()
    {
        var hint = Dir("hint");
        var root = Dir("root");
        Assert.Equal(hint, Browser(root).StartPath(hint));
    }

    [Fact]
    public void StartPath_resolves_a_relative_hint_against_root()
    {
        var root = Dir("root");
        var sub = Dir("root", "proj");
        Assert.Equal(sub, Browser(root).StartPath("proj"));
    }

    [Fact]
    public void StartPath_falls_back_to_root_when_the_hint_does_not_exist()
    {
        var root = Dir("root");
        Assert.Equal(root, Browser(root).StartPath(Path.Combine(_tree, "missing")));
        Assert.Equal(root, Browser(root).StartPath(null));
    }

    [Fact]
    public void StartPath_falls_back_to_home_without_hint_or_existing_root()
    {
        Assert.Equal(_home, Browser(Path.Combine(_tree, "no-root")).StartPath("whatever"));
        Assert.Equal(_home, Browser().StartPath("relative-without-root"));
    }
}
