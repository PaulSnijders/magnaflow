namespace MagnaFlow.MfWatch.Notify;

/// <summary>The extra notification channel (ontwerp-v0.1.md "Notifications"); console output via
/// the logger always happens regardless of whether a notifier is configured.</summary>
public interface INotifier
{
    Task NotifyAsync(string title, string message);
}
