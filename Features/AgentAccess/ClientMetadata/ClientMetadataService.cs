using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using OpenIddict.Abstractions;
using Serilog;

namespace Hub.Features.AgentAccess;

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
            var redirectKnown = redirectUri is null
                                || (await applications.GetRedirectUrisAsync(existing, cancellationToken)).Contains(redirectUri, StringComparer.Ordinal);
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

            if (document.RedirectUris.Any(r => !options.IsTrustedRedirectHost(new Uri(r).Host)))
            {
                const string reason = "A redirect URI is not on a trusted host.";
                Log.Warning("Client metadata for {ClientId} rejected: {Reason}", clientId, reason);
                failures.Remember(clientId, new ClientMetadataFailure(reason, Transient: false), options.NegativeTtl);
                return KeepOrReject(fetchedAt, transient: false, reason);
            }

            var descriptor = AgentAccessClientSeeder.ToDescriptor(new AgentAccessClientOptions
            {
                ClientId = document.ClientId,
                DisplayName = document.ClientName,
                RedirectUris = [.. document.RedirectUris]
            });
            descriptor.Properties[MarkerProperty] = JsonSerializer.SerializeToElement(time.GetUtcNow().ToString("O"));

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
