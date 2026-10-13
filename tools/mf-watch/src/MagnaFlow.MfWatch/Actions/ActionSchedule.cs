namespace MagnaFlow.MfWatch.Actions;

/// <summary>
/// The due rules of docs/specs/concepts/actions.md "When an action runs", pure: everything comes in
/// as arguments, so tests need no clock and no time zone of their own.
/// <list type="bullet">
/// <item>every_minutes: due when there is no run yet, or the last run started N minutes ago or more.</item>
/// <item>at: due when the last run started before the most recent HH:mm (local). Without a run the
/// watcher's start counts as the last run, so a new machine does not fire a nightly job at noon;
/// a watcher that was down at HH:mm catches up once.</item>
/// </list>
/// </summary>
public static class ActionSchedule
{
    /// <summary>When the action is (or was) next due; null for a manual-only action. Due means
    /// this is at or before now.</summary>
    public static DateTimeOffset? NextDue(
        WatchAction action, DateTimeOffset? lastRunAt, DateTimeOffset watcherStart, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (action.EveryMinutes is { } minutes)
            return lastRunAt is { } last ? last.AddMinutes(minutes) : now;

        if (action.At is not { } at)
            return null;

        var baseline = lastRunAt ?? watcherStart;
        var recent = MostRecent(at, now, zone);
        return baseline < recent ? recent : Occurrence(at, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(recent, zone).DateTime).AddDays(1), zone);
    }

    public static bool IsDue(WatchAction action, DateTimeOffset? lastRunAt, DateTimeOffset watcherStart, DateTimeOffset now, TimeZoneInfo zone) =>
        NextDue(action, lastRunAt, watcherStart, now, zone) is { } due && due <= now;

    /// <summary>The latest HH:mm (local) at or before now.</summary>
    private static DateTimeOffset MostRecent(TimeOnly at, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var candidate = Occurrence(at, today, zone);
        return candidate <= now ? candidate : Occurrence(at, today.AddDays(-1), zone);
    }

    private static DateTimeOffset Occurrence(TimeOnly at, DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(at, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
