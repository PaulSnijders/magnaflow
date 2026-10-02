namespace MagnaFlow.MfWatch.Polling;

/// <summary>
/// "Look now": a one-shot wake signal at <c>.magnaflow/mf-watch.wake</c> (ontwerp-v0.1.md
/// "Adaptive polling"). Backing off to interval_max is right for an idle project, but it leaves a
/// human who *just* made a command ready waiting up to 15 minutes. Any writer that can create an
/// empty file can cut that short — <c>touch .magnaflow/mf-watch.wake</c> by hand, or mf-cockpit's
/// "Check now" button; nothing here knows or cares which.
///
/// The daemon's sleep is therefore sliced instead of being one uninterruptible delay, and it is
/// the *delete* that makes the wake one-shot: an undeleted file would wake every poll forever.
/// A wake is deliberately not "activity" — the caller keeps feeding the BackoffScheduler the
/// poll's own outcome, so asking to look does not itself reset the interval or the idle window.
/// </summary>
public static class WakeFile
{
    /// <summary>How long a wake can wait before it is noticed, at most.</summary>
    public static readonly TimeSpan DefaultSlice = TimeSpan.FromSeconds(1);

    public static string PathFor(string projectRoot) => Path.Combine(projectRoot, ".magnaflow", "mf-watch.wake");

    /// <summary>True when a wake was requested; deletes the file so it fires exactly once.</summary>
    public static bool TryConsume(string projectRoot)
    {
        var path = PathFor(projectRoot);
        if (!File.Exists(path))
            return false;

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false; // still held open by whoever wrote it — the next slice tries again
        }
    }

    /// <summary>Sleeps <paramref name="duration"/> in slices, returning early (true) as soon as a
    /// wake file shows up. False means the full interval simply elapsed.</summary>
    public static async Task<bool> SleepAsync(
        TimeSpan duration, string projectRoot, CancellationToken cancellationToken, TimeSpan? slice = null)
    {
        var step = slice ?? DefaultSlice;
        var remaining = duration;
        while (remaining > TimeSpan.Zero)
        {
            var next = remaining < step ? remaining : step;
            await Task.Delay(next, cancellationToken);
            remaining -= next;
            if (TryConsume(projectRoot))
                return true;
        }
        return false;
    }
}
