using Microsoft.Extensions.Caching.Distributed;

namespace Hub.Features.AgentAccess;

// The Redis keys apps check before honouring an agent request (phase 1.6). Redis is a cache of OpenIddict state,
// never the source of truth: a flushed Redis only costs the next refresh a re-set.
public interface IAgentGrantStore
{
    /// <summary>Sets or extends agent-grant:{authorizationId}. Called on consent, every refresh and every exchange.</summary>
    Task MarkLiveAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<bool> IsLiveAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task RevokeAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>True while agent-access:tenant:{tenantId}:off exists.</summary>
    Task<bool> IsTenantOffAsync(int tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raises (or renews) agent-access:tenant:{tenantId}:off, the flag every app checks on every agent request. There is
    /// deliberately no way to clear it: it only ever fails closed, and it expires by itself.
    /// </summary>
    Task SetTenantOffAsync(int tenantId, CancellationToken cancellationToken = default);
}

public sealed class AgentGrantStore(IDistributedCache cache) : IAgentGrantStore
{
    // Longer than the 20 minute access token, so a key set at the last refresh outlives the token it came with.
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan TenantOffLifetime = TimeSpan.FromMinutes(30);

    public Task MarkLiveAsync(string authorizationId, CancellationToken cancellationToken = default) =>
        cache.SetAsync(
            GrantKey(authorizationId),
            [1],
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = KeyLifetime },
            cancellationToken);

    public async Task<bool> IsLiveAsync(string authorizationId, CancellationToken cancellationToken = default) =>
        IsSafeId(authorizationId) && await cache.GetAsync(GrantKey(authorizationId), cancellationToken) is not null;

    public Task RevokeAsync(string authorizationId, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(GrantKey(authorizationId), cancellationToken);

    public async Task<bool> IsTenantOffAsync(int tenantId, CancellationToken cancellationToken = default) =>
        await cache.GetAsync($"agent-access:tenant:{tenantId}:off", cancellationToken) is not null;

    // Raised whenever the tenant is seen to be off, so it outlives the 15 minute app pass it must stop. Nothing clears
    // it: a clear could race a switch-off and leave agents running, so it simply expires once nobody has re-asserted it
    // (an admin who switches back on waits at most this long). A flush loses it along with every grant key, which the
    // apps also refuse on, so agents stay stopped until the next refresh re-checks the tenant.
    public Task SetTenantOffAsync(int tenantId, CancellationToken cancellationToken = default) =>
        cache.SetAsync(
            $"agent-access:tenant:{tenantId}:off",
            [1],
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TenantOffLifetime },
            cancellationToken);

    private static string GrantKey(string authorizationId)
    {
        if (!IsSafeId(authorizationId)) throw new ArgumentException("The authorization id is not valid.", nameof(authorizationId));
        return "agent-grant:" + authorizationId;
    }

    private static bool IsSafeId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
