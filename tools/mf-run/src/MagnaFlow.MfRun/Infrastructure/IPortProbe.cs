using System.Net.Sockets;

namespace MagnaFlow.MfRun.Infrastructure;

/// <summary>
/// The second, independent status signal (docs/prompts/0003 "Port probe"): a service the OS is
/// actually answering on, regardless of whether mf-run's own PID file agrees. A plain TCP connect
/// attempt against localhost — no HTTP, no new dependency — proves nothing about the response, only
/// that something is listening.
/// </summary>
public interface IPortProbe
{
    Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken = default);
}

public sealed class TcpPortProbe : IPortProbe
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(300);

    public async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectTimeout);
        try
        {
            await client.ConnectAsync("127.0.0.1", port, timeoutCts.Token);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // connection refused, timed out, or any other failure to connect: nothing is listening
            return false;
        }
    }
}
