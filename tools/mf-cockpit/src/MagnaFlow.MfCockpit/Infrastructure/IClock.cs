namespace MagnaFlow.MfCockpit.Infrastructure;

/// <summary>Seam over wall-clock time — draft `created:` stamps and stale-`running` detection
/// need it, and both are exercised in tests without real sleeping (same role as
/// MagnaFlow.MfWatch.Infrastructure.IClock).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
