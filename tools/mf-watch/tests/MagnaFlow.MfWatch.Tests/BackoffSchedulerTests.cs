using MagnaFlow.MfWatch.Polling;

namespace MagnaFlow.MfWatch.Tests;

public class BackoffSchedulerTests
{
    private static BackoffScheduler Create(FakeClock clock, int minMinutes = 1, int maxMinutes = 15, int idleGraceMinutes = 30) =>
        new(TimeSpan.FromMinutes(minMinutes), TimeSpan.FromMinutes(maxMinutes), TimeSpan.FromMinutes(idleGraceMinutes), clock);

    [Fact]
    public void StartsAtIntervalMin()
    {
        var scheduler = Create(new FakeClock());
        Assert.Equal(TimeSpan.FromMinutes(1), scheduler.Current);
    }

    [Fact]
    public void StaysAtMinWhileActivityContinues()
    {
        var clock = new FakeClock();
        var scheduler = Create(clock);

        for (var i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            scheduler.RecordPoll(activity: true);
            Assert.Equal(TimeSpan.FromMinutes(1), scheduler.Current);
        }
    }

    [Fact]
    public void StaysAtMinDuringEmptyPollsWithinIdleGrace()
    {
        var clock = new FakeClock();
        var scheduler = Create(clock, idleGraceMinutes: 30);

        // Empty polls, but total elapsed time stays under idle_grace.
        for (var i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            scheduler.RecordPoll(activity: false);
        }

        Assert.Equal(TimeSpan.FromMinutes(1), scheduler.Current);
    }

    [Fact]
    public void DoublesAfterIdleGraceElapsesOnEmptyPolls()
    {
        var clock = new FakeClock();
        var scheduler = Create(clock, minMinutes: 1, maxMinutes: 15, idleGraceMinutes: 5);

        clock.Advance(TimeSpan.FromMinutes(1));
        scheduler.RecordPoll(activity: false); // idle window starts now
        Assert.Equal(TimeSpan.FromMinutes(1), scheduler.Current);

        clock.Advance(TimeSpan.FromMinutes(5)); // grace elapsed
        scheduler.RecordPoll(activity: false);
        Assert.Equal(TimeSpan.FromMinutes(2), scheduler.Current);

        scheduler.RecordPoll(activity: false); // grace still elapsed, another empty poll
        Assert.Equal(TimeSpan.FromMinutes(4), scheduler.Current);

        scheduler.RecordPoll(activity: false);
        Assert.Equal(TimeSpan.FromMinutes(8), scheduler.Current);
    }

    [Fact]
    public void CapsAtIntervalMax()
    {
        var clock = new FakeClock();
        var scheduler = Create(clock, minMinutes: 1, maxMinutes: 15, idleGraceMinutes: 0);

        for (var i = 0; i < 10; i++)
            scheduler.RecordPoll(activity: false);

        Assert.Equal(TimeSpan.FromMinutes(15), scheduler.Current);
    }

    [Fact]
    public void ActivityResetsIntervalAndIdleWindow()
    {
        var clock = new FakeClock();
        var scheduler = Create(clock, minMinutes: 1, maxMinutes: 15, idleGraceMinutes: 0);

        scheduler.RecordPoll(activity: false);
        scheduler.RecordPoll(activity: false);
        Assert.True(scheduler.Current > TimeSpan.FromMinutes(1));

        scheduler.RecordPoll(activity: true);
        Assert.Equal(TimeSpan.FromMinutes(1), scheduler.Current);

        // Idle window restarted: an immediately following empty poll should not double yet
        // if idle_grace hasn't elapsed again. Use a non-zero grace to observe this precisely.
    }

    [Fact]
    public void ActivityAfterBackoffRestartsIdleGraceWindow()
    {
        var clock = new FakeClock();
        var scheduler = Create(clock, minMinutes: 1, maxMinutes: 15, idleGraceMinutes: 5);

        clock.Advance(TimeSpan.FromMinutes(10));
        scheduler.RecordPoll(activity: false); // idle since t=10
        clock.Advance(TimeSpan.FromMinutes(10));
        scheduler.RecordPoll(activity: false); // grace elapsed, doubles
        Assert.Equal(TimeSpan.FromMinutes(2), scheduler.Current);

        scheduler.RecordPoll(activity: true); // activity resets everything
        Assert.Equal(TimeSpan.FromMinutes(1), scheduler.Current);

        clock.Advance(TimeSpan.FromMinutes(1));
        scheduler.RecordPoll(activity: false); // fresh idle window, grace not yet elapsed
        Assert.Equal(TimeSpan.FromMinutes(1), scheduler.Current);
    }
}
