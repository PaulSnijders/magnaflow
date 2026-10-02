using System.Text.Json;
using MagnaFlow.MfRun.Runtime;

namespace MagnaFlow.MfRun.Tests;

public class StatusJsonTests
{
    [Fact]
    public void SerializesRunningServiceWithPidAndUrl()
    {
        var json = StatusJson.Serialize([new ServiceStatusEntry("web", true, 4242, "http://localhost:5000")]);

        using var doc = JsonDocument.Parse(json);
        var entry = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("web", entry.GetProperty("name").GetString());
        Assert.True(entry.GetProperty("running").GetBoolean());
        Assert.Equal(4242, entry.GetProperty("pid").GetInt32());
        Assert.Equal("http://localhost:5000", entry.GetProperty("url").GetString());
    }

    [Fact]
    public void OmitsPidAndUrlWhenStoppedAndUnconfigured()
    {
        var json = StatusJson.Serialize([new ServiceStatusEntry("api", false, null, null)]);

        using var doc = JsonDocument.Parse(json);
        var entry = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("api", entry.GetProperty("name").GetString());
        Assert.False(entry.GetProperty("running").GetBoolean());
        Assert.False(entry.TryGetProperty("pid", out _));
        Assert.False(entry.TryGetProperty("url", out _));
    }

    [Fact]
    public void EmptyListSerializesToEmptyArray()
    {
        Assert.Equal("[]", StatusJson.Serialize([]));
    }

    [Fact]
    public void SerializesReasonAndPortListeningWhenStopped()
    {
        var json = StatusJson.Serialize([new ServiceStatusEntry("api", false, null, null, "no-pid-file", false)]);

        using var doc = JsonDocument.Parse(json);
        var entry = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("no-pid-file", entry.GetProperty("reason").GetString());
        Assert.False(entry.GetProperty("portListening").GetBoolean());
    }

    [Fact]
    public void OmitsReasonAndPortListeningWhenNotSet()
    {
        var json = StatusJson.Serialize([new ServiceStatusEntry("web", true, 4242, null)]);

        using var doc = JsonDocument.Parse(json);
        var entry = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.False(entry.TryGetProperty("reason", out _));
        Assert.False(entry.TryGetProperty("portListening", out _));
    }

    [Fact]
    public void MultipleServicesPreserveOrder()
    {
        var json = StatusJson.Serialize([
            new ServiceStatusEntry("api", true, 1, null),
            new ServiceStatusEntry("web", false, null, "http://localhost:5000"),
        ]);

        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Equal(["api", "web"], names);
    }
}
