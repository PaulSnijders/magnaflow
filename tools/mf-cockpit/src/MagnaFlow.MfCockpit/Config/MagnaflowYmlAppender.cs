using System.Text.RegularExpressions;

namespace MagnaFlow.MfCockpit.Config;

/// <summary>
/// Write #6's registration step (ontwerp-v0.4.md "Writing magnaflow.yml"): a narrow, structured
/// text edit that appends one entry to `cockpit: projects:` — never a parse-and-regenerate, which
/// would destroy comments and ordering (the same reasoning that made write #5 a raw editor).
/// Callers are responsible for serializing concurrent calls with a single lock across scaffold +
/// register (ontwerp-v0.4.md "Guards") — this class does no locking of its own.
/// </summary>
public static class MagnaflowYmlAppender
{
    public enum Outcome { Ok, LegacyFileRefused, VerifyFailed }

    public sealed record AppendResult(Outcome Outcome, string Path, string? Error);

    /// <summary>
    /// Resolves which file write #6 targets, from the already-loaded CockpitConfig (ontwerp-v0.4.md
    /// "Writing magnaflow.yml"'s three cases): the legacy mf-cockpit.yml filename is refused
    /// outright; an existing magnaflow.yml (wherever LocatePath found it) is edited in place;
    /// nothing found at all means a fresh magnaflow.yml is created in the user config dir, not next
    /// to the binary, since that location may not be writable.
    /// </summary>
    public static Task<AppendResult> AppendProjectAsync(CockpitConfig config, ProjectEntry entry, string? userConfigDirectory = null) =>
        AppendProjectAsync(config, entry, userConfigDirectory, ComposeAppendedContent);

    /// <summary>Test seam: a caller-supplied composer stands in for ComposeAppendedContent, so the
    /// .bak-write / reparse-verify / restore-on-failure path can be proven with a deliberately
    /// broken edit instead of relying on ComposeAppendedContent happening to fail somehow.</summary>
    public static async Task<AppendResult> AppendProjectAsync(
        CockpitConfig config, ProjectEntry entry, string? userConfigDirectory, Func<string, ProjectEntry, string> composer)
    {
        if (config.IsLegacyFileName)
        {
            return new AppendResult(Outcome.LegacyFileRefused, config.ConfigPath,
                $"{config.ConfigPath} is the deprecated mf-cockpit.yml filename — merge it into magnaflow.yml under a top-level 'cockpit:' section before adding projects here (see docs/fase7-machine-config/).");
        }

        var targetPath = File.Exists(config.ConfigPath)
            ? config.ConfigPath
            : Path.Combine(userConfigDirectory ?? CockpitConfig.DefaultUserConfigDirectory(), CockpitConfig.FileName);

        var fileExisted = File.Exists(targetPath);
        var original = fileExisted ? await File.ReadAllTextAsync(targetPath) : "";
        var edited = composer(original, entry);

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var backupPath = targetPath + ".bak";
        if (fileExisted)
            File.Copy(targetPath, backupPath, overwrite: true);

        try
        {
            await File.WriteAllTextAsync(targetPath, edited);

            var (reloaded, loadError, _) = CockpitConfig.Load(targetPath);
            var roundTrips = reloaded is not null && reloaded.Projects.Any(p =>
                string.Equals(p.Name, entry.Name, StringComparison.Ordinal) &&
                string.Equals(Path.GetFullPath(p.Path), Path.GetFullPath(entry.Path), StringComparison.OrdinalIgnoreCase));

            if (!roundTrips)
            {
                Restore(targetPath, backupPath, fileExisted);
                return new AppendResult(Outcome.VerifyFailed, targetPath,
                    loadError ?? "the appended entry did not round-trip on reparse");
            }

            return new AppendResult(Outcome.Ok, targetPath, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Restore(targetPath, backupPath, fileExisted);
            return new AppendResult(Outcome.VerifyFailed, targetPath, ex.Message);
        }
        finally
        {
            if (fileExisted)
                TryDelete(backupPath);
        }
    }

    public enum RemoveOutcome { Ok, LegacyFileRefused, FileNotFound, VerifyFailed }

    public sealed record RemoveResult(RemoveOutcome Outcome, string Path, string? Error);

    /// <summary>Write #7's registration removal (ontwerp-v0.5.md item 4): the mirror of
    /// AppendProjectAsync — remove exactly one entry from `cockpit: projects:` (or a legacy
    /// root-level `projects:`) as a text edit, `.bak` first, then re-parse and verify the entry is
    /// gone AND every other entry survived; restore + report VerifyFailed on any failure. Same
    /// no-locking-of-its-own posture as the appender: the caller holds the single config-write lock.
    /// The legacy mf-cockpit.yml filename is refused, exactly as write #6 does.</summary>
    public static Task<RemoveResult> RemoveProjectAsync(CockpitConfig config, string name, string? userConfigDirectory = null) =>
        RemoveProjectAsync(config, name, userConfigDirectory, ComposeRemovedContent);

    /// <summary>Test seam: a caller-supplied composer stands in for ComposeRemovedContent, so the
    /// .bak-write / reparse-verify / restore-on-failure path can be proven with a deliberately
    /// broken edit (e.g. one that drops a sibling entry) instead of relying on the real composer.</summary>
    public static async Task<RemoveResult> RemoveProjectAsync(
        CockpitConfig config, string name, string? userConfigDirectory, Func<string, string, string> composer)
    {
        if (config.IsLegacyFileName)
        {
            return new RemoveResult(RemoveOutcome.LegacyFileRefused, config.ConfigPath,
                $"{config.ConfigPath} is the deprecated mf-cockpit.yml filename — merge it into magnaflow.yml under a top-level 'cockpit:' section before managing projects here (see docs/fase7-machine-config/).");
        }

        var targetPath = File.Exists(config.ConfigPath)
            ? config.ConfigPath
            : Path.Combine(userConfigDirectory ?? CockpitConfig.DefaultUserConfigDirectory(), CockpitConfig.FileName);

        if (!File.Exists(targetPath))
            return new RemoveResult(RemoveOutcome.FileNotFound, targetPath, $"no magnaflow.yml found at {targetPath} to remove the entry from");

        var original = await File.ReadAllTextAsync(targetPath);
        var (before, _, _) = CockpitConfig.Load(targetPath);
        var beforeNames = before?.Projects.Select(p => p.Name).ToList() ?? [];
        if (!beforeNames.Contains(name, StringComparer.Ordinal))
            return new RemoveResult(RemoveOutcome.VerifyFailed, targetPath, $"'{name}' is not present in {targetPath}");

        var edited = composer(original, name);

        var backupPath = targetPath + ".bak";
        File.Copy(targetPath, backupPath, overwrite: true);
        try
        {
            await File.WriteAllTextAsync(targetPath, edited);

            var (after, loadError, _) = CockpitConfig.Load(targetPath);
            var afterNames = after?.Projects.Select(p => p.Name).ToList();
            var ok = after is not null && afterNames is not null
                && !afterNames.Contains(name, StringComparer.Ordinal)
                && beforeNames.Where(n => !string.Equals(n, name, StringComparison.Ordinal))
                    .All(n => afterNames.Contains(n, StringComparer.Ordinal));

            if (!ok)
            {
                File.Copy(backupPath, targetPath, overwrite: true);
                return new RemoveResult(RemoveOutcome.VerifyFailed, targetPath,
                    loadError ?? "the entry was not removed cleanly (or another entry was lost) on reparse");
            }

            return new RemoveResult(RemoveOutcome.Ok, targetPath, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.Copy(backupPath, targetPath, overwrite: true);
            return new RemoveResult(RemoveOutcome.VerifyFailed, targetPath, ex.Message);
        }
        finally
        {
            TryDelete(backupPath);
        }
    }

    /// <summary>The pure removal edit — no file I/O, unit-testable on strings. Removes the single
    /// `- name: &lt;name&gt;` list item (and its nested lines, e.g. `path:`) whose enclosing key is
    /// `projects:`, wherever that block sits (root-level legacy or under `cockpit:`). Everything
    /// around it — comments, ordering, other entries — is preserved byte-for-byte. Removing the only
    /// entry leaves an empty `projects:` block, which is valid and re-addable.</summary>
    public static string ComposeRemovedContent(string original, string name)
    {
        var eol = original.Contains("\r\n") ? "\r\n" : "\n";
        List<string> lines = original.Length == 0 ? [] : [.. original.Replace("\r\n", "\n").Split('\n')];
        var hadTrailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (hadTrailingNewline)
            lines.RemoveAt(lines.Count - 1);

        for (var i = 0; i < lines.Count; i++)
        {
            if (IsBlankOrComment(lines[i]))
                continue;
            var markerIndent = LeadingSpaces(lines[i]);
            var content = lines[i][markerIndent..];
            if (!content.StartsWith("- ", StringComparison.Ordinal))
                continue;

            var afterDash = content[2..].TrimStart();
            if (!afterDash.StartsWith("name:", StringComparison.Ordinal))
                continue;
            var value = StripQuotes(afterDash["name:".Length..].Trim());
            if (!string.Equals(value, name, StringComparison.Ordinal))
                continue;
            if (!EnclosingKeyIs(lines, i, markerIndent, "projects"))
                continue;

            var end = i + 1;
            while (end < lines.Count && lines[end].Length > 0 && LeadingSpaces(lines[end]) > markerIndent)
                end++;
            lines.RemoveRange(i, end - i);
            break; // names are unique — remove exactly the first match
        }

        return lines.Count == 0 ? "" : string.Join(eol, lines) + eol;
    }

    private static string StripQuotes(string s) =>
        s.Length >= 2 && (s[0] == '"' && s[^1] == '"' || s[0] == '\'' && s[^1] == '\'') ? s[1..^1] : s;

    /// <summary>Whether the nearest non-blank line shallower than the given item's marker indent is
    /// `key:` — i.e. the list item is a direct child of that key.</summary>
    private static bool EnclosingKeyIs(List<string> lines, int itemIndex, int markerIndent, string key)
    {
        for (var j = itemIndex - 1; j >= 0; j--)
        {
            if (IsBlankOrComment(lines[j]))
                continue;
            var spaces = LeadingSpaces(lines[j]);
            if (spaces < markerIndent)
            {
                var content = lines[j][spaces..];
                return content == $"{key}:" || content.StartsWith($"{key}:", StringComparison.Ordinal);
            }
        }
        return false;
    }

    private static void Restore(string targetPath, string backupPath, bool fileExisted)
    {
        if (fileExisted)
            File.Copy(backupPath, targetPath, overwrite: true);
        else
            TryDelete(targetPath);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>
    /// The pure text edit — no file I/O, unit-testable directly on strings. Implements the four
    /// shapes from ontwerp-v0.4.md "Writing magnaflow.yml": an existing `projects:` block list
    /// (append an entry), an inline `projects: []` (replace with a block list), a `cockpit:`
    /// section without the key (insert it), no `cockpit:` section at all (append one). Indentation
    /// is read from the surrounding structure, not assumed, so hand-formatted files round-trip.
    /// </summary>
    public static string ComposeAppendedContent(string original, ProjectEntry entry)
    {
        var eol = original.Contains("\r\n") ? "\r\n" : "\n";
        List<string> lines = original.Length == 0 ? [] : [.. original.Replace("\r\n", "\n").Split('\n')];
        var hadTrailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (hadTrailingNewline)
            lines.RemoveAt(lines.Count - 1);

        var cockpitIndex = FindKeyAtIndent(lines, 0, 0, "cockpit");
        if (cockpitIndex < 0)
        {
            // Shape 4: no `cockpit:` section at all — append a fresh one at EOF.
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
                lines.Add("");
            lines.Add("cockpit:");
            lines.Add("  projects:");
            lines.AddRange(FormatEntry(entry, indent: 4));
        }
        else
        {
            var cockpitBlockEnd = FindBlockEnd(lines, cockpitIndex, indent: 0);
            var childIndent = FirstChildIndent(lines, cockpitIndex + 1, cockpitBlockEnd) ?? 2;
            var projectsIndex = FindKeyAtIndent(lines, cockpitIndex + 1, childIndent, "projects");

            if (projectsIndex < 0 || projectsIndex >= cockpitBlockEnd)
            {
                // Shape 3: `cockpit:` section present, but no `projects:` key — insert one as the
                // section's first child, right after the `cockpit:` line itself.
                var inserted = new List<string> { $"{Indent(childIndent)}projects:" };
                inserted.AddRange(FormatEntry(entry, indent: childIndent + 2));
                lines.InsertRange(cockpitIndex + 1, inserted);
            }
            else if (IsInlineEmptyList(lines[projectsIndex], childIndent))
            {
                // Shape 2: `projects: []` — replace with a block list.
                var replacement = new List<string> { $"{Indent(childIndent)}projects:" };
                replacement.AddRange(FormatEntry(entry, indent: childIndent + 2));
                lines.RemoveAt(projectsIndex);
                lines.InsertRange(projectsIndex, replacement);
            }
            else
            {
                // Shape 1: an existing `projects:` block list — append after its last entry.
                var projectsBlockEnd = FindBlockEnd(lines, projectsIndex, childIndent);
                var itemIndent = FirstChildIndent(lines, projectsIndex + 1, projectsBlockEnd) ?? childIndent + 2;
                lines.InsertRange(projectsBlockEnd, FormatEntry(entry, itemIndent));
            }
        }

        return string.Join(eol, lines) + eol;
    }

    private static IEnumerable<string> FormatEntry(ProjectEntry entry, int indent) =>
    [
        $"{Indent(indent)}- name: {entry.Name}",
        $"{Indent(indent + 2)}path: {entry.Path.Replace('\\', '/')}",
    ];

    private static string Indent(int width) => new(' ', width);

    private static bool IsBlankOrComment(string line)
    {
        var trimmed = line.TrimStart(' ');
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static int LeadingSpaces(string line) => line.Length - line.TrimStart(' ').Length;

    private static readonly Regex InlineEmptyList = new(@"^projects:\s*\[\s*\]\s*$", RegexOptions.Compiled);

    private static bool IsInlineEmptyList(string line, int indent) =>
        LeadingSpaces(line) == indent && InlineEmptyList.IsMatch(line.TrimStart(' '));

    /// <summary>Finds `key:` at exactly the given indent, scanning from startIndex — stops (returns
    /// -1) the moment a non-blank, non-comment line at a shallower indent is seen, i.e. once the
    /// enclosing block has ended.</summary>
    private static int FindKeyAtIndent(List<string> lines, int startIndex, int indent, string key)
    {
        for (var i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsBlankOrComment(line))
                continue;
            var spaces = LeadingSpaces(line);
            if (spaces < indent)
                return -1;
            if (spaces == indent)
            {
                var content = line[spaces..];
                if (content == $"{key}:" || content.StartsWith($"{key}: ", StringComparison.Ordinal) || content.StartsWith($"{key}:\t", StringComparison.Ordinal))
                    return i;
            }
        }
        return -1;
    }

    /// <summary>Exclusive end index of the block owned by the key at keyIndex (its indent given by
    /// `indent`): every following blank/comment line, or line indented deeper than `indent`, is
    /// still part of the block; the first non-blank line at indent &lt;= `indent` (or EOF) ends it.</summary>
    private static int FindBlockEnd(List<string> lines, int keyIndex, int indent)
    {
        for (var i = keyIndex + 1; i < lines.Count; i++)
        {
            if (IsBlankOrComment(lines[i]))
                continue;
            if (LeadingSpaces(lines[i]) <= indent)
                return i;
        }
        return lines.Count;
    }

    /// <summary>The indent of the first non-blank/comment line in [startIndex, endIndex) — null if
    /// the range has no such line (an empty block).</summary>
    private static int? FirstChildIndent(List<string> lines, int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            if (IsBlankOrComment(lines[i]))
                continue;
            return LeadingSpaces(lines[i]);
        }
        return null;
    }
}
