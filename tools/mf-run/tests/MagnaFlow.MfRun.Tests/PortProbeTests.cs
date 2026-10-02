using System.Net;
using System.Net.Sockets;
using MagnaFlow.MfRun.Infrastructure;

namespace MagnaFlow.MfRun.Tests;

/// <summary>Exercises the real TcpPortProbe against an actual socket, rather than a fake — the
/// FakePortProbe used elsewhere assumes the seam's contract is correct; this proves it.</summary>
public class PortProbeTests
{
    [Fact]
    public async Task ReportsTrueWhenSomethingListensOnThePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new TcpPortProbe();

            var listening = await probe.IsListeningAsync(port);

            Assert.True(listening);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ReportsFalseWhenNothingListensOnThePort()
    {
        // bind to grab a genuinely free port, then release it — nothing is listening there anymore
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var freePort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var probe = new TcpPortProbe();

        var listening = await probe.IsListeningAsync(freePort);

        Assert.False(listening);
    }
}
