namespace MagnaFlow.MfCockpit.Prompts;

/// <summary>
/// The NNNN-name id, decomposed. A follow-up command (v0.2) widens the numeric prefix from
/// exactly 4 digits to 4 digits plus an optional single uppercase letter B-Z (0005, 0005B, 0005C,
/// ...) — everything that used to assume a fixed 4-character prefix (Number/Name splitting, the
/// title fallback) needs to find the actual hyphen instead of hardcoding an offset. Own copy, not
/// shared with MagnaFlow.WorkerController — same file-format-only knowledge as every other
/// cockpit reader (ontwerp-v0.1.md "Tech").
/// </summary>
internal static class IdShape
{
    /// <summary>Length of the numeric+optional-letter prefix before the separating hyphen: 5 when
    /// id[4] is a follow-up letter (B-Z), else 4. Assumes id already matches the lane's id grammar.</summary>
    public static int NumberLength(string id) => id.Length > 4 && id[4] is >= 'B' and <= 'Z' ? 5 : 4;
}
