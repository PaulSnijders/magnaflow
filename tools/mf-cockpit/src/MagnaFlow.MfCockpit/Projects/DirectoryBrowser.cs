using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Models;

namespace MagnaFlow.MfCockpit.Projects;

/// <summary>
/// The Add project → Existing folder picker (docs/specs/cockpit/index.md, "Browse"): lists the
/// subfolders of one directory on the cockpit's own machine. Read-only, and only directory names
/// leave it — never files. Hidden folders (Hidden attribute or a leading '.') are left out.
/// </summary>
public sealed class DirectoryBrowser(NewProjectConfig newProjectConfig, string homeDirectory)
{
    public enum ErrorKind { None, BadPath, NotFound, Denied }

    public sealed record Result(BrowseListingDto? Listing, ErrorKind Error, string? Message)
    {
        public static Result Fail(ErrorKind kind, string message) => new(null, kind, message);
    }

    /// <summary>The folder the picker opens on: the hint (resolved like Existing resolves a path)
    /// when it is an existing directory, else new_project.root when it exists, else home.</summary>
    public string StartPath(string? hint)
    {
        var root = newProjectConfig.Root;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var resolved = TryResolve(hint.Trim(), root);
            if (resolved is not null && Directory.Exists(resolved))
                return resolved;
        }

        if (!string.IsNullOrWhiteSpace(root))
        {
            var fullRoot = TryFullPath(root);
            if (fullRoot is not null && Directory.Exists(fullRoot))
                return fullRoot;
        }

        return homeDirectory;
    }

    public Result List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return Result.Fail(ErrorKind.BadPath, "path must be absolute");

        var full = TryFullPath(path);
        if (full is null)
            return Result.Fail(ErrorKind.BadPath, $"'{path}' is not a valid path");

        if (!Directory.Exists(full))
            return Result.Fail(ErrorKind.NotFound, $"'{full}' does not exist or is not a directory");

        List<BrowseDirDto> dirs;
        try
        {
            dirs = [];
            foreach (var sub in new DirectoryInfo(full).EnumerateDirectories())
            {
                if (IsHidden(sub))
                    continue;
                dirs.Add(new BrowseDirDto(sub.Name, sub.FullName, IsGitRepo(sub.FullName)));
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Fail(ErrorKind.Denied, $"'{full}' cannot be read");
        }
        catch (DirectoryNotFoundException)
        {
            return Result.Fail(ErrorKind.NotFound, $"'{full}' does not exist or is not a directory");
        }

        dirs.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        var parent = Directory.GetParent(full)?.FullName;
        return new Result(new BrowseListingDto(full, parent, Roots(), dirs), ErrorKind.None, null);
    }

    private static bool IsHidden(DirectoryInfo sub)
    {
        if (sub.Name.StartsWith('.'))
            return true;
        try { return (sub.Attributes & FileAttributes.Hidden) != 0; }
        catch { return false; } // an unreadable subfolder is still listed (spec)
    }

    // Worktrees and submodules have a .git *file*; a probe that throws just means "not a repo".
    private static bool IsGitRepo(string dir)
    {
        try
        {
            var git = Path.Combine(dir, ".git");
            return Directory.Exists(git) || File.Exists(git);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> Roots()
    {
        if (!OperatingSystem.IsWindows())
            return ["/"];

        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                    roots.Add(drive.RootDirectory.FullName);
            }
            catch
            {
                // a drive that throws on probing is simply not offered
            }
        }
        return roots;
    }

    // Same resolution as NewProjectEndpoints' Existing mode: rooted as is, relative against root.
    private static string? TryResolve(string path, string? root)
    {
        if (Path.IsPathRooted(path))
            return TryFullPath(path);
        return string.IsNullOrWhiteSpace(root) ? null : TryFullPath(Path.Combine(root, path));
    }

    private static string? TryFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }
}
