using System.Text.RegularExpressions;

namespace MagnaFlow.MfCockpit.Projects;

/// <summary>
/// Turns a project name into a filesystem-safe directory name (ontwerp-v0.4.md "Creating a
/// project": "trim, strip characters invalid in Windows file names, spaces → -, collapse
/// repeats"). No override field — the dialog previews the result; rename by hand later if it
/// matters (KISS).
/// </summary>
public static class DirNameSanitizer
{
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex HyphenRun = new(@"-{2,}", RegexOptions.Compiled);

    public static string Sanitize(string? name)
    {
        var trimmed = (name ?? "").Trim();
        var stripped = new string(trimmed.Where(c => !InvalidChars.Contains(c) && !char.IsControl(c)).ToArray());
        var hyphenated = WhitespaceRun.Replace(stripped, "-");
        var collapsed = HyphenRun.Replace(hyphenated, "-");
        return collapsed.Trim('-');
    }

    public static bool IsReservedName(string dirName) =>
        ReservedNames.Contains(dirName, StringComparer.OrdinalIgnoreCase);
}
