namespace MagnaFlow.MfRun.Infrastructure;

/// <summary>Seam for time and waiting so the ~2s start-liveness check is testable without a real sleep.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    Task Delay(TimeSpan duration, CancellationToken cancellationToken = default);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task Delay(TimeSpan duration, CancellationToken cancellationToken = default) =>
        Task.Delay(duration, cancellationToken);
}
