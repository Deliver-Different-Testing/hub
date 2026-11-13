using System.Text.Json;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.ClientMetadata;

public sealed record ClientMetadataOutcome(bool Ready, string? Reason = null)
{
    public static ClientMetadataOutcome Ok { get; } = new(true);

    public static ClientMetadataOutcome Rejected(string reason) => new(false, reason);
}

public interface IClientMetadataService
{
    /// <summary>
    /// Makes sure an application row exists for a client ID metadata document URL, fetching and validating the
    /// document when needed. Never throws for a bad document or a failed fetch: those are a rejected outcome.
    /// </summary>
    Task<ClientMetadataOutcome> EnsureAsync(string clientId, string? redirectUri, CancellationToken cancellationToken = default);
}

// Client ID metadata documents (phase 1.8): a URL used as client_id is fetched, validated and cached as an ordinary
// application row, so OpenIddict validates redirect URI, PKCE and permissions exactly as for a registered client.
public sealed class ClientMetadataService(
    IOpenIddictApplicationManager applications,
    IClientMetadataFetcher fetcher,
    ClientMetadataOptions options,
    ClientMetadataFailures failures,
    TimeProvider time) : IClientMetadataService
{
    private const string MarkerProperty = "cimd_fetched_utc";
    private const int MaxConcurrentFetches = 4;
    private const int MaxSigningKeys = 10;
    private static readonly SemaphoreSlim FetchGate = new(MaxConcurrentFetches);

    public async Task<ClientMetadataOutcome> EnsureAsync(string clientId, string? redirectUri, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return ClientMetadataOutcome.Rejected("Client ID metadata documents are not enabled.");
        if (!ClientMetadataDocument.IsAcceptableClientIdUrl(clientId, out var url)) return ClientMetadataOutcome.Rejected("The client_id is not an acceptable URL.");
        if (!options.IsAllowed(url)) return ClientMetadataOutcome.Rejected("This client URL is not allowed.");

        var existing = await applications.FindByClientIdAsync(clientId, cancellationToken);
        DateTimeOffset? fetchedAt = null;
        if (existing is not null)
        {
            var properties = await applications.GetPropertiesAsync(existing, cancellationToken);
            if (!properties.TryGetValue(MarkerProperty, out var marker) || !DateTimeOffset.TryParse(marker.GetString(), out var parsed))
            {
                return ClientMetadataOutcome.Ok;
            }

            fetchedAt = parsed;
            var age = time.GetUtcNow() - parsed;
            // OpenIddict's own check, so a native client's loopback redirect matches on any port.
            var redirectKnown = redirectUri is null || await applications.ValidateRedirectUriAsync(existing, redirectUri, cancellationToken);
            if (age < options.Ttl && (redirectKnown || age < options.RefetchFloor))
            {
                return ClientMetadataOutcome.Ok;
            }
        }

        if (failures.TryGet(clientId, out var previous))
        {
            return KeepOrReject(fetchedAt, previous!.Transient, previous.Reason);
        }

        // Never queue: when every slot is busy the request is refused rather than piling up behind a slow server.
        if (!await FetchGate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return ClientMetadataOutcome.Rejected("Too many client documents are being fetched; try again shortly.");
        }

        try
        {
            var fetched = await fetcher.FetchAsync(url, cancellationToken);
            var parsedDocument = fetched.Body is null ? ClientMetadataParseResult.Fail(fetched.Error ?? "The fetch failed.") : ClientMetadataDocument.Parse(fetched.Body, clientId);
            if (parsedDocument.Document is not { } document)
            {
                var transient = fetched.Body is null;
                Log.Warning("Client metadata for {ClientId} rejected: {Reason}", clientId, parsedDocument.Error);
                failures.Remember(clientId, new ClientMetadataFailure(parsedDocument.Error!, transient), options.NegativeTtl);
                return KeepOrReject(fetchedAt, transient, parsedDocument.Error!);
            }

            // A loopback redirect returns the code to the signed-in user's own machine, and PKCE is required.
            var redirects = document.RedirectUris.Select(r => new Uri(r)).ToList();
            if (redirects.Any(r => !r.IsLoopback && !options.IsTrustedRedirectHost(r.Host)))
            {
                const string reason = "A redirect URI is not on a trusted host.";
                Log.Warning("Client metadata for {ClientId} rejected: {Reason}", clientId, reason);
                failures.Remember(clientId, new ClientMetadataFailure(reason, Transient: false), options.NegativeTtl);
                return KeepOrReject(fetchedAt, transient: false, reason);
            }

            // A private_key_jwt client's keys are fetched with its document and are only as trusted as it: same hosts, same
            // fetcher. The token endpoint never fetches, so new keys arrive with the next authorization request.
            JsonWebKeySet? signingKeys = null;
            if (document.JwksUri is { } jwksUri)
            {
                var keys = await FetchSigningKeysAsync(jwksUri, cancellationToken);
                if (keys.Error is { } keyError)
                {
                    Log.Warning("Client metadata for {ClientId} rejected: {Reason}", clientId, keyError);
                    failures.Remember(clientId, new ClientMetadataFailure(keyError, keys.Transient), options.NegativeTtl);
                    return KeepOrReject(fetchedAt, keys.Transient, keyError);
                }

                signingKeys = keys.Keys;
            }

            var descriptor = AgentAccessClientSeeder.ToDescriptor(new AgentAccessClientOptions
            {
                ClientId = document.ClientId,
                DisplayName = document.ClientName,
                RedirectUris = [.. document.RedirectUris]
            }, signingKeys);
            descriptor.Properties[MarkerProperty] = JsonSerializer.SerializeToElement(time.GetUtcNow().ToString("O"));
            // Native lets OpenIddict accept any port on a loopback redirect (RFC 8252 section 7.3).
            descriptor.ApplicationType = redirects.Any(r => r.IsLoopback) ? ApplicationTypes.Native : ApplicationTypes.Web;

            if (existing is null)
            {
                await applications.CreateAsync(descriptor, cancellationToken);
                Log.Information("Client metadata application {ClientId} created", clientId);
            }
            else
            {
                await applications.UpdateAsync(existing, descriptor, cancellationToken);
            }

            return ClientMetadataOutcome.Ok;
        }
        finally
        {
            FetchGate.Release();
        }
    }

    private async Task<(JsonWebKeySet? Keys, string? Error, bool Transient)> FetchSigningKeysAsync(Uri jwksUri, CancellationToken cancellationToken)
    {
        if (!options.IsTrustedRedirectHost(jwksUri.Host))
        {
            return (null, "The jwks_uri is not on a trusted host.", false);
        }

        var fetched = await fetcher.FetchAsync(jwksUri, cancellationToken);
        if (fetched.Body is null) return (null, $"The key set could not be fetched: {fetched.Error}", true);

        JsonWebKeySet set;
        try
        {
            set = new JsonWebKeySet(fetched.Body);
        }
        catch (ArgumentException)
        {
            return (null, "The key set is not a valid JWK set.", false);
        }

        // A published private key means the client is compromised or broken; refuse the whole set.
        if (set.Keys.Any(k => k.HasPrivateKey || !string.IsNullOrEmpty(k.D) || !string.IsNullOrEmpty(k.K)))
        {
            return (null, "The key set contains private or symmetric key material.", false);
        }

        var usable = new JsonWebKeySet();
        foreach (var key in set.Keys.Where(k => k.Kty is "RSA" or "EC" && k.Use is null or "sig").Take(MaxSigningKeys))
        {
            usable.Keys.Add(key);
        }

        return usable.Keys.Count == 0
            ? (null, "The key set has no public signing key.", false)
            : (usable, null, false);
    }

    // Only a transport failure (timeout, 5xx, network) keeps serving the last known good row, and only for two
    // hours. A document that was reached and is no longer acceptable ends the client straight away: a client that
    // went away or turned hostile must not live on from the cache. Grants already made are unaffected either way.
    private ClientMetadataOutcome KeepOrReject(DateTimeOffset? fetchedAt, bool transient, string reason) =>
        transient && fetchedAt is { } at && time.GetUtcNow() - at < options.MaxStale
            ? ClientMetadataOutcome.Ok
            : ClientMetadataOutcome.Rejected(reason);
}

public sealed record ClientMetadataFailure(string Reason, bool Transient);

// Remembers recent failures so a bad client URL is not fetched again for a few minutes. Bounded, so a flood of
// distinct URLs cannot grow it without limit.
public sealed class ClientMetadataFailures : IDisposable
{
    private const int MaxEntries = 1000;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });

    public bool TryGet(string clientId, out ClientMetadataFailure? failure) => _cache.TryGetValue(clientId, out failure);

    public void Remember(string clientId, ClientMetadataFailure failure, TimeSpan ttl) =>
        _cache.Set(clientId, failure, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = ttl });

    public void Forget(string clientId) => _cache.Remove(clientId);

    public void Dispose() => _cache.Dispose();
}
