using Hub.Features.AgentAccess.Grants;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Grants;

// Phase 1.6: the Redis liveness key apps check. Redis is only a cache of OpenIddict state, never the source of truth.
public class AgentGrantStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (AgentGrantStore Store, IDistributedCache Cache) Build()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return (new AgentGrantStore(cache), cache);
    }

    [Fact]
    public async Task A_grant_is_live_after_it_is_marked_and_not_before()
    {
        var (store, _) = Build();

        Assert.False(await store.IsLiveAsync("auth-1", Ct));
        await store.MarkLiveAsync("auth-1", Ct);

        Assert.True(await store.IsLiveAsync("auth-1", Ct));
        Assert.False(await store.IsLiveAsync("auth-2", Ct));
    }

    [Fact]
    public async Task The_key_is_exactly_agent_grant_followed_by_the_authorization_id()
    {
        var (store, cache) = Build();

        await store.MarkLiveAsync("0d7b2c9e-1111-2222-3333-444455556666", Ct);

        Assert.NotNull(await cache.GetAsync("agent-grant:0d7b2c9e-1111-2222-3333-444455556666", Ct));
    }

    [Fact]
    public async Task Revoking_removes_the_key()
    {
        var (store, _) = Build();
        await store.MarkLiveAsync("auth-1", Ct);

        await store.RevokeAsync("auth-1", Ct);

        Assert.False(await store.IsLiveAsync("auth-1", Ct));
    }

    [Fact]
    public async Task Marking_again_just_extends_it()
    {
        var (store, _) = Build();

        await store.MarkLiveAsync("auth-1", Ct);
        await store.MarkLiveAsync("auth-1", Ct);

        Assert.True(await store.IsLiveAsync("auth-1", Ct));
    }

    [Fact]
    public async Task The_key_expires_after_thirty_minutes_unless_refreshed()
    {
        var cache = Substitute.For<IDistributedCache>();

        await new AgentGrantStore(cache).MarkLiveAsync("auth-1", Ct);

        await cache.Received(1).SetAsync(
            "agent-grant:auth-1",
            Arg.Any<byte[]>(),
            Arg.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(30)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a:b")]
    [InlineData("a b")]
    public async Task Ids_that_could_alter_the_key_are_never_written(string id)
    {
        var (store, cache) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() => store.MarkLiveAsync(id, Ct));
        Assert.False(await store.IsLiveAsync(id, Ct));
        Assert.Null(await cache.GetAsync("agent-grant:" + id, Ct));
    }

    [Fact]
    public async Task Switching_a_tenant_off_raises_the_flag_for_that_tenant_only()
    {
        var (store, cache) = Build();

        await store.SetTenantOffAsync(12, Ct);

        Assert.True(await store.IsTenantOffAsync(12, Ct));
        Assert.NotNull(await cache.GetAsync("agent-access:tenant:12:off", Ct));
        Assert.False(await store.IsTenantOffAsync(13, Ct));
    }

    [Fact]
    public async Task The_flag_lives_thirty_minutes_unless_renewed_which_outlasts_a_fifteen_minute_app_pass()
    {
        var cache = Substitute.For<IDistributedCache>();

        await new AgentGrantStore(cache).SetTenantOffAsync(12, Ct);

        await cache.Received(1).SetAsync(
            "agent-access:tenant:12:off",
            Arg.Any<byte[]>(),
            Arg.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(30)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_store_has_no_way_to_clear_the_flag()
    {
        Assert.DoesNotContain(typeof(IAgentGrantStore).GetMethods(), m => m.Name.Contains("Clear") || m.Name.Contains("TenantOn"));
        Assert.Single(typeof(IAgentGrantStore).GetMethods(), m => m.Name == "SetTenantOffAsync");
    }

    [Fact]
    public async Task The_tenant_off_flag_reads_the_tenant_key_and_is_off_by_default()
    {
        var (store, cache) = Build();

        Assert.False(await store.IsTenantOffAsync(12, Ct));
        await cache.SetAsync("agent-access:tenant:12:off", [1], Ct);

        Assert.True(await store.IsTenantOffAsync(12, Ct));
        Assert.False(await store.IsTenantOffAsync(13, Ct));
    }
}
