using MagnaFlow.MfWatch.Actions;

namespace MagnaFlow.MfWatch.Tests;

public class ActionScheduleTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Started = Now.AddHours(-1);

    private static readonly WatchAction Every10 = new("board", 10, null);
    private static WatchAction At(int h, int m) => new("nightly", null, new TimeOnly(h, m));

    [Fact]
    public void Every_minutes_without_a_run_is_due()
    {
        Assert.True(ActionSchedule.IsDue(Every10, null, Started, Now, Utc));
    }

    [Theory]
    [InlineData(-9, false)]
    [InlineData(-10, true)]
    [InlineData(-30, true)]
    public void Every_minutes_is_due_n_minutes_after_the_last_start(int minutesAgo, bool due)
    {
        Assert.Equal(due, ActionSchedule.IsDue(Every10, Now.AddMinutes(minutesAgo), Started, Now, Utc));
    }

    [Fact]
    public void Every_minutes_next_due_is_the_last_start_plus_n()
    {
        Assert.Equal(Now.AddMinutes(4), ActionSchedule.NextDue(Every10, Now.AddMinutes(-6), Started, Now, Utc));
    }

    [Fact]
    public void Manual_only_is_never_due()
    {
        var manual = new WatchAction("fix", null, null);
        Assert.False(ActionSchedule.IsDue(manual, null, Started, Now, Utc));
        Assert.Null(ActionSchedule.NextDue(manual, null, Started, Now, Utc));
    }

    [Fact]
    public void At_never_run_counts_the_watcher_start_so_a_new_machine_waits()
    {
        // Started at 11:00, nightly at 02:30: the most recent 02:30 is before the start.
        Assert.False(ActionSchedule.IsDue(At(2, 30), null, Started, Now, Utc));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 2, 30, 0, TimeSpan.Zero),
            ActionSchedule.NextDue(At(2, 30), null, Started, Now, Utc));
    }

    [Fact]
    public void At_never_run_fires_once_the_time_passes_after_the_start()
    {
        // Started at 11:00, at 11:30, now 12:00.
        Assert.True(ActionSchedule.IsDue(At(11, 30), null, Started, Now, Utc));
    }

    [Fact]
    public void At_ran_before_todays_time_is_due()
    {
        Assert.True(ActionSchedule.IsDue(At(10, 0), Now.AddDays(-1), Started, Now, Utc));
    }

    [Fact]
    public void At_ran_after_todays_time_is_not_due_until_tomorrow()
    {
        var ranAt = new DateTimeOffset(2026, 10, 13, 10, 5, 0, TimeSpan.Zero);
        Assert.False(ActionSchedule.IsDue(At(10, 0), ranAt, Started, Now, Utc));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero),
            ActionSchedule.NextDue(At(10, 0), ranAt, Started, Now, Utc));
    }

    [Fact]
    public void At_later_today_is_not_due_yet()
    {
        var ranYesterday = new DateTimeOffset(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);
        Assert.False(ActionSchedule.IsDue(At(18, 0), ranYesterday, Started, Now, Utc));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero),
            ActionSchedule.NextDue(At(18, 0), ranYesterday, Started, Now, Utc));
    }

    [Fact]
    public void A_missed_night_catches_up_once()
    {
        var threeNightsAgo = new DateTimeOffset(2026, 10, 10, 2, 30, 0, TimeSpan.Zero);
        Assert.True(ActionSchedule.IsDue(At(2, 30), threeNightsAgo, Started, Now, Utc));

        // It runs now; the next poll is not due again.
        Assert.False(ActionSchedule.IsDue(At(2, 30), Now, Started, Now.AddMinutes(1), Utc));
    }

    [Fact]
    public void At_uses_local_time()
    {
        var amsterdam = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
        // 12:00 UTC is 14:00 local; at 13:30 local was 11:30 UTC, after the 11:00 start.
        Assert.True(ActionSchedule.IsDue(At(13, 30), null, Started, Now, amsterdam));
        Assert.False(ActionSchedule.IsDue(At(14, 30), null, Started, Now, amsterdam));
    }
}
