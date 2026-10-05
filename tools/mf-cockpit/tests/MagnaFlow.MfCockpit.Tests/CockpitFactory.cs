using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>
/// Hosts the real app in-process (TestServer, no real socket) against a disposable config file
/// (ontwerp-v0.1.md's API integration tests: "a disposable project fixture"). MF_COCKPIT_CONFIG is
/// how Program.cs's own config-path resolution is redirected for a test run
/// (Config/CockpitConfig.LocatePath), since WebApplicationFactory re-executes Program.cs's
/// top-level code in-process rather than passing test-controlled argv.
/// </summary>
public sealed class CockpitFactory(string configPath, Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("MF_COCKPIT_CONFIG", configPath);
        // A fresh directory per host build — the real %APPDATA%\MagnaFlow location must never be
        // touched by a test run (Config/CockpitConfig.DefaultUserConfigDirectory, mirrored by
        // Infrastructure/FileCockpitLog).
        Environment.SetEnvironmentVariable("MF_COCKPIT_LOG_DIR",
            Path.Combine(Path.GetTempPath(), "mf-cockpit-tests", Guid.NewGuid().ToString("N") + "-log"));
        return base.CreateHost(builder);
    }

    /// <summary>Lets a test swap a real seam (e.g. IRunClient) for a fake, instead of spawning a
    /// real process for scenarios that only exercise endpoint wiring, not the process itself.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        if (configureServices is not null)
            builder.ConfigureServices(configureServices);
    }

    public static string WriteConfig(string path, params (string Name, string ProjectRoot)[] projects) =>
        WriteConfig(path, chat: null, run: null, projects);

    public static string WriteConfig(string path, (string Command, double TimeoutMinutes)? chat, params (string Name, string ProjectRoot)[] projects) =>
        WriteConfig(path, chat, run: null, projects);

    public static string WriteConfig(
        string path,
        (string Command, double TimeoutMinutes)? chat,
        (string Command, double TimeoutSeconds)? run,
        params (string Name, string ProjectRoot)[] projects)
    {
        var lines = new List<string> { "projects:" };
        foreach (var (name, root) in projects)
        {
            lines.Add($"  - name: {name}");
            lines.Add($"    path: {root.Replace("\\", "/")}");
        }
        if (chat is { } c)
        {
            lines.Add("chat:");
            lines.Add($"  command: {c.Command.Replace("\\", "/")}");
            lines.Add($"  timeout_minutes: {c.TimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        if (run is { } r)
        {
            lines.Add("run:");
            lines.Add($"  command: {r.Command.Replace("\\", "/")}");
            lines.Add($"  timeout_seconds: {r.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        File.WriteAllText(path, Sectioned(lines));
        return path;
    }

    public sealed record NewProjectTemplateSpec(
        string Name, string Type, string? Source = null, string? Command = null, string[]? Args = null, double? TimeoutSeconds = null);

    public sealed record NewProjectSpec(string? Root, string? SpecKit, NewProjectTemplateSpec[]? Templates);

    /// <summary>write #6's own config shape (cockpit.new_project) — a separate helper rather than
    /// another WriteConfig overload parameter, since WriteConfig's params array must stay last.</summary>
    public static string WriteConfigWithNewProject(string path, NewProjectSpec newProject, params (string Name, string ProjectRoot)[] projects)
    {
        var lines = new List<string> { "projects:" };
        foreach (var (name, root) in projects)
        {
            lines.Add($"  - name: {name}");
            lines.Add($"    path: {root.Replace("\\", "/")}");
        }

        lines.Add("new_project:");
        if (!string.IsNullOrWhiteSpace(newProject.Root))
            lines.Add($"  root: {newProject.Root!.Replace("\\", "/")}");
        if (!string.IsNullOrWhiteSpace(newProject.SpecKit))
            lines.Add($"  spec_kit: {newProject.SpecKit!.Replace("\\", "/")}");
        if (newProject.Templates is { Length: > 0 })
        {
            lines.Add("  templates:");
            foreach (var t in newProject.Templates)
            {
                lines.Add($"    - name: {t.Name}");
                lines.Add($"      type: {t.Type}");
                if (!string.IsNullOrWhiteSpace(t.Source))
                    lines.Add($"      source: {t.Source!.Replace("\\", "/")}");
                if (!string.IsNullOrWhiteSpace(t.Command))
                    lines.Add($"      command: {t.Command!.Replace("\\", "/")}");
                if (t.Args is { Length: > 0 })
                    lines.Add($"      args: [{string.Join(", ", t.Args.Select(a => $"\"{a}\""))}]");
                if (t.TimeoutSeconds is { } ts)
                    lines.Add($"      timeout_seconds: {ts.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
        }

        File.WriteAllText(path, Sectioned(lines));
        return path;
    }

    /// <summary>Wraps the cockpit fields in a top-level `cockpit:` section — the current format, and
    /// the only one Add project appends to (a flat root is refused, see MagnaflowYmlAppender).</summary>
    private static string Sectioned(IEnumerable<string> lines) =>
        string.Join('\n', new[] { "cockpit:" }.Concat(lines.Select(l => "  " + l)));
}
