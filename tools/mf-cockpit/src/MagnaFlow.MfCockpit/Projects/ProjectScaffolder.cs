using MagnaFlow.MfCockpit.Config;
using MagnaFlow.MfCockpit.Infrastructure;
using MagnaFlow.MfCockpit.Prompts;

namespace MagnaFlow.MfCockpit.Projects;

/// <summary>
/// Write #6's "mode: new" pipeline (ontwerp-v0.4.md "Creating a project"): validate, mkdir, run the
/// optional code template, copy the spec kit, write hygiene files where the template didn't already
/// provide them, git init + initial commit, then seed the adopt draft via the existing write-#1
/// path (DraftWriter — no new mechanism). Registration (config append + in-memory registry) is
/// deliberately the caller's job, done only after ScaffoldAsync succeeds — ontwerp-v0.4.md
/// "Registration is deliberately last: a failed scaffold never leaves a phantom entry."
/// </summary>
public sealed class ProjectScaffolder(IProcessRunner processRunner, IGitClient git, DraftWriter draftWriter, NewProjectConfig newProjectConfig)
{
    /// <summary>
    /// Everything under .magnaflow/ is machine-local — watcher log/lock, mf-run PID files, the
    /// worker's per-command logs and session.yml (an agent transcript handle bound to one machine
    /// and one checkout path, so useless in git and actively harmful: elsewhere it resolves and
    /// then fails inside the agent instead of starting fresh). config.yml is the exception: it is
    /// project-level worker config and travels in git. Order matters — the negation must follow the
    /// wildcard. Ignored rather than untracked because untracked files dirty the tree mf-watch
    /// polls, and the worker's dirty-tree guard then blocks every run.
    /// </summary>
    private static readonly string[] RuntimeIgnoreLines =
    [
        "# MagnaFlow runtime plane - machine-local (logs, session ids, PID files)",
        ".magnaflow/*",
        "!.magnaflow/config.yml",
    ];

    private const string ConfigStub = """
        # .magnaflow/config.yml — worker config for this project. Run
        # docs/spec-kit/0001-adopt-spec-system.md as a Claude Code prompt to fill
        # this in with the project's actual build/test commands (step 3b), or
        # edit it by hand.
        #
        # build:
        #   command: <build command>
        # test:
        #   command: <test command>
        # defaults:
        #   max_attempts: 3
        #   command_timeout_minutes: 30
        # agent:
        #   command: claude
        #   args: []
        """ + "\n";

    public enum ValidationOutcome
    {
        Ok, RootNotConfigured, NameRequired, NameNotUnique, DirNameEmpty, DirNameReserved, TargetExists, TemplateNotFound,
    }

    public sealed record ValidationResult(ValidationOutcome Outcome, string? DirName, string? TargetPath, string? Error)
    {
        public bool IsOk => Outcome == ValidationOutcome.Ok;
    }

    /// <summary>Validation order is the design (ontwerp-v0.4.md "Creating a project" step 1): name
    /// required and unique, sanitized dirname non-empty and not a reserved device name, target dir
    /// must not exist, template name (if given) must exist in config. Root-configured is checked
    /// just before it's needed (dirname/target both depend on it).</summary>
    public ValidationResult Validate(string name, string? templateName, ProjectRegistry registry)
    {
        var trimmedName = (name ?? "").Trim();
        if (trimmedName.Length == 0)
            return new ValidationResult(ValidationOutcome.NameRequired, null, null, "name is required");
        if (registry.ExistsByName(trimmedName))
            return new ValidationResult(ValidationOutcome.NameNotUnique, null, null, $"a project named '{trimmedName}' is already registered");
        if (string.IsNullOrWhiteSpace(newProjectConfig.Root))
            return new ValidationResult(ValidationOutcome.RootNotConfigured, null, null, "cockpit.new_project.root is not configured");

        var dirName = DirNameSanitizer.Sanitize(trimmedName);
        if (dirName.Length == 0)
            return new ValidationResult(ValidationOutcome.DirNameEmpty, null, null, "the sanitized directory name is empty — choose a name with at least one valid character");
        if (DirNameSanitizer.IsReservedName(dirName))
            return new ValidationResult(ValidationOutcome.DirNameReserved, dirName, null, $"'{dirName}' is a reserved Windows device name — choose a different name");

        var targetPath = Path.Combine(newProjectConfig.Root, dirName);
        if (Directory.Exists(targetPath) || File.Exists(targetPath))
            return new ValidationResult(ValidationOutcome.TargetExists, dirName, targetPath, $"'{targetPath}' already exists");

        if (!string.IsNullOrWhiteSpace(templateName) && FindTemplate(templateName) is null)
            return new ValidationResult(ValidationOutcome.TemplateNotFound, dirName, targetPath, $"no template named '{templateName}' is configured");

        return new ValidationResult(ValidationOutcome.Ok, dirName, targetPath, null);
    }

    private NewProjectTemplate? FindTemplate(string name) =>
        newProjectConfig.Templates.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    public sealed record ScaffoldResult(
        bool Success, string TargetPath, IReadOnlyList<string> Warnings, string? Error, string? TemplateOutput, DraftWriter.CreateResult? SeededDraft);

    /// <summary>Steps 2-7 of ontwerp-v0.4.md "Creating a project". A failed step leaves the
    /// directory in place, unregistered, with the captured output/error for diagnosis — the caller
    /// never registers on Success=false.</summary>
    public async Task<ScaffoldResult> ScaffoldAsync(string projectName, string dirName, string targetPath, string? templateName, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        Directory.CreateDirectory(targetPath);

        if (!string.IsNullOrWhiteSpace(templateName))
        {
            var template = FindTemplate(templateName)!; // caller already validated it exists
            var (ok, output, error) = await RunTemplateAsync(template, targetPath, projectName, cancellationToken);
            if (!ok)
                return new ScaffoldResult(false, targetPath, warnings, error, output, null);
        }

        if (!string.IsNullOrWhiteSpace(newProjectConfig.SpecKit))
        {
            if (Directory.Exists(newProjectConfig.SpecKit))
                CopyDirectoryContents(newProjectConfig.SpecKit, Path.Combine(targetPath, "docs", "spec-kit"), skipTopLevelGit: true);
            else
                warnings.Add($"cockpit.new_project.spec_kit '{newProjectConfig.SpecKit}' does not exist — spec kit was not copied");
        }
        else
        {
            warnings.Add("cockpit.new_project.spec_kit is not configured — spec kit was not copied");
        }

        EnsureHygieneFiles(targetPath);

        var gitDir = Path.Combine(targetPath, ".git");
        if (!Directory.Exists(gitDir))
        {
            var initResult = await processRunner.RunExecutableAsync("git", ["init", "-q"], targetPath, timeout: TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);
            if (!initResult.Succeeded)
                return new ScaffoldResult(false, targetPath, warnings, $"git init failed: {initResult.StdErr.Trim()}", null, null);
        }

        try
        {
            await git.CommitAllAsync(targetPath, $"cockpit: create project {projectName}");
        }
        catch (GitException ex)
        {
            return new ScaffoldResult(false, targetPath, warnings, $"initial commit failed: {ex.Message}", null, null);
        }

        DraftWriter.CreateResult? seeded = null;
        try
        {
            const string body = "Adopt mf-spec in this fresh project — run `docs/spec-kit/0001-adopt-spec-system.md` as a prompt in Claude Code.\n";
            seeded = await draftWriter.CreateDraftAsync(targetPath, "Adopt spec system", body, group: null, specs: null);
        }
        catch (Exception ex) when (ex is ArgumentException or GitException)
        {
            warnings.Add($"could not seed the adopt draft: {ex.Message}");
        }

        return new ScaffoldResult(true, targetPath, warnings, null, null, seeded);
    }

    private async Task<(bool Ok, string? Output, string? Error)> RunTemplateAsync(
        NewProjectTemplate template, string targetPath, string projectName, CancellationToken cancellationToken)
    {
        if (string.Equals(template.Type, "copy", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(template.Source) || !Directory.Exists(template.Source))
                return (false, null, $"template '{template.Name}': source '{template.Source}' does not exist");

            CopyDirectoryContents(template.Source, targetPath, skipTopLevelGit: true);
            return (true, null, null);
        }

        if (string.Equals(template.Type, "command", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(template.Command))
                return (false, null, $"template '{template.Name}': no command configured");

            var args = template.Args.Select(a => a.Replace("{target}", targetPath).Replace("{name}", projectName)).ToList();
            var result = await processRunner.RunExecutableAsync(template.Command, args, targetPath, timeout: template.Timeout, cancellationToken: cancellationToken);
            var output = string.Join('\n', new[] { result.StdOut.Trim(), result.StdErr.Trim() }.Where(s => s.Length > 0));

            if (result.TimedOut)
                return (false, output, $"template '{template.Name}' timed out after {template.Timeout.TotalSeconds:0}s");
            if (result.ExitCode != 0)
                return (false, output, $"template '{template.Name}' exited {result.ExitCode}");
            return (true, output, null);
        }

        return (false, null, $"template '{template.Name}': unknown type '{template.Type}'");
    }

    /// <summary>Recursive copy of source's *contents* into destination (ontwerp-v0.4.md "Machine
    /// config": "copy — recursive copy of source's contents"), skipping a top-level `.git` when the
    /// source happens to be a working copy — nested `.git` directories (submodules, unlikely) are
    /// left alone since skipTopLevelGit is only honored at the top call.</summary>
    private static void CopyDirectoryContents(string source, string destination, bool skipTopLevelGit)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (skipTopLevelGit && string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectoryContents(dir, Path.Combine(destination, name), skipTopLevelGit: false);
        }
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    /// <summary>ontwerp-v0.4.md "Creating a project" step 5: hygiene files only where the template
    /// didn't already provide them. For .gitignore that means per missing *line*, not per file —
    /// same posture as mf-spec's own adopt/update prompts (0001/0002-*.md step 3c): create the file
    /// if absent, otherwise add only whichever of the three runtime-ignore lines are missing, never
    /// touching an existing line. For config.yml it's whole-file: never overwrite one the template
    /// already left behind.</summary>
    private static void EnsureHygieneFiles(string targetPath)
    {
        var gitignorePath = Path.Combine(targetPath, ".gitignore");
        List<string> existingLines = File.Exists(gitignorePath) ? [.. File.ReadAllLines(gitignorePath)] : [];
        var missing = RuntimeIgnoreLines.Where(line => !existingLines.Any(existing => existing.Trim() == line)).ToList();
        if (missing.Count > 0)
        {
            if (existingLines.Count > 0 && existingLines[^1].Trim().Length != 0)
                existingLines.Add("");
            existingLines.AddRange(missing);
            File.WriteAllLines(gitignorePath, existingLines);
        }

        var configPath = Path.Combine(targetPath, ".magnaflow", "config.yml");
        if (!File.Exists(configPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, ConfigStub);
        }
    }
}
