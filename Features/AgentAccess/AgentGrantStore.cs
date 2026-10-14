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
}

public sealed class AgentGrantStore(IDistributedCache cache) : IAgentGrantStore
{
    // Longer than the 20 minute access token, so a key set at the last refresh outlives the token it came with.
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromMinutes(30);

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

    private static string GrantKey(string authorizationId)
    {
        if (!IsSafeId(authorizationId)) throw new ArgumentException("The authorization id is not valid.", nameof(authorizationId));
        return "agent-grant:" + authorizationId;
    }

    private static bool IsSafeId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
