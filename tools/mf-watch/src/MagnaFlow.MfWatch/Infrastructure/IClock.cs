namespace MagnaFlow.MfWatch.Infrastructure;

/// <summary>Seam for time so the backoff logic is testable without real sleeping.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
