using MagnaFlow.MfCockpit.Infrastructure;

namespace MagnaFlow.MfCockpit.Tests;

/// <summary>The per-project TTL memo in front of the two process-spawning endpoints
/// (docs/prompts/0010). FakeClock is what makes expiry testable without sleeping.</summary>
public class TtlCacheTests
{
    private readonly FakeClock _clock = new();

    private TtlCache<string> Cache(int seconds = 3) => new(_clock, TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task Second_read_within_the_ttl_does_not_call_the_factory_again()
    {
        var cache = Cache();
        var calls = 0;

        var first = await cache.GetOrAddAsync("proj", () => { calls++; return Task.FromResult("v1"); });
        _clock.UtcNow = _clock.UtcNow.AddSeconds(2);
        var second = await cache.GetOrAddAsync("proj", () => { calls++; return Task.FromResult("v2"); });

        Assert.Equal("v1", first);
        Assert.Equal("v1", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Read_after_the_ttl_recomputes()
    {
        var cache = Cache();

        Assert.Equal("v1", await cache.GetOrAddAsync("proj", () => Task.FromResult("v1")));
        _clock.UtcNow = _clock.UtcNow.AddSeconds(4);

        Assert.Equal("v2", await cache.GetOrAddAsync("proj", () => Task.FromResult("v2")));
    }

    [Fact]
    public async Task Invalidate_drops_only_the_named_key()
    {
        var cache = Cache();
        await cache.GetOrAddAsync("a", () => Task.FromResult("a1"));
        await cache.GetOrAddAsync("b", () => Task.FromResult("b1"));

        cache.Invalidate("a");

        Assert.Equal("a2", await cache.GetOrAddAsync("a", () => Task.FromResult("a2")));
        Assert.Equal("b1", await cache.GetOrAddAsync("b", () => Task.FromResult("b2")));
    }

    [Fact]
    public async Task Invalidating_an_unknown_key_is_a_no_op()
    {
        var cache = Cache();
        cache.Invalidate("never-cached");

        Assert.Equal("v1", await cache.GetOrAddAsync("proj", () => Task.FromResult("v1")));
    }

    [Fact]
    public async Task A_failure_is_not_remembered_for_the_rest_of_the_ttl()
    {
        var cache = Cache();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrAddAsync("proj", () => Task.FromException<string>(new InvalidOperationException("git exploded"))));

        // Same instant, so the entry would still be "fresh" had the faulted call been cached.
        Assert.Equal("v1", await cache.GetOrAddAsync("proj", () => Task.FromResult("v1")));
    }

    [Fact]
    public async Task Concurrent_misses_share_one_call()
    {
        var cache = Cache();
        var calls = 0;
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var a = cache.GetOrAddAsync("proj", () => { calls++; return gate.Task; });
        var b = cache.GetOrAddAsync("proj", () => { calls++; return gate.Task; });
        gate.SetResult("v1");

        Assert.Equal("v1", await a);
        Assert.Equal("v1", await b);
        Assert.Equal(1, calls); // one git subprocess for two simultaneous readers, not two
    }
}
