using MagnaFlow.MfWatch.Infrastructure;

namespace MagnaFlow.MfWatch.Polling;

/// <summary>
/// Adaptive poll interval (ontwerp-v0.1.md "Adaptive polling"): stays at intervalMin as long as
/// anything is happening; only once idleGrace has elapsed with no activity does it start
/// doubling per empty poll, up to intervalMax. Any activity resets both the interval and the
/// idle window immediately. Pure state machine driven by an injected IClock — no sleeping here,
/// so it is unit-testable by advancing a fake clock and calling RecordPoll.
/// </summary>
public sealed class BackoffScheduler
{
    private readonly TimeSpan _intervalMin;
    private readonly TimeSpan _intervalMax;
    private readonly TimeSpan _idleGrace;
    private readonly IClock _clock;
    private DateTimeOffset? _idleSince;

    public BackoffScheduler(TimeSpan intervalMin, TimeSpan intervalMax, TimeSpan idleGrace, IClock clock)
    {
        _intervalMin = intervalMin;
        _intervalMax = intervalMax < intervalMin ? intervalMin : intervalMax;
        _idleGrace = idleGrace;
        _clock = clock;
        Current = intervalMin;
    }

    public TimeSpan Current { get; private set; }

    /// <summary>Call once after each poll cycle with whether that poll did anything
    /// (a pull brought commits, or a command was run).</summary>
    public void RecordPoll(bool activity)
    {
        var now = _clock.UtcNow;
        if (activity)
        {
            Current = _intervalMin;
            _idleSince = null;
            return;
        }

        _idleSince ??= now;
        if (now - _idleSince.Value >= _idleGrace)
        {
            var doubled = Current + Current;
            Current = doubled > _intervalMax || doubled <= TimeSpan.Zero ? _intervalMax : doubled;
        }
    }
}
