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
    IMemoryCache failures,
    TimeProvider time) : IClientMetadataService
{
    private const string MarkerProperty = "cimd_fetched_utc";
    private static readonly SemaphoreSlim FetchGate = new(4);

    public async Task<ClientMetadataOutcome> EnsureAsync(string clientId, string? redirectUri, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return ClientMetadataOutcome.Rejected("Client ID metadata documents are not enabled.");
        if (!ClientMetadataDocument.IsAcceptableClientIdUrl(clientId, out var url)) return ClientMetadataOutcome.Rejected("The client_id is not an acceptable URL.");
        if (!options.IsAllowed(url.Host)) return ClientMetadataOutcome.Rejected("This client host is not allowed.");

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

        if (failures.TryGetValue(FailureKey(clientId), out string? previous))
        {
            return KeepOrReject(fetchedAt, previous!);
        }

        await FetchGate.WaitAsync(cancellationToken);
        try
        {
            var fetched = await fetcher.FetchAsync(url, cancellationToken);
            var parsedDocument = fetched.Body is null ? ClientMetadataParseResult.Fail(fetched.Error ?? "The fetch failed.") : ClientMetadataDocument.Parse(fetched.Body, clientId);
            if (parsedDocument.Document is not { } document)
            {
                Log.Warning("Client metadata for {ClientId} rejected: {Reason}", clientId, parsedDocument.Error);
                failures.Set(FailureKey(clientId), parsedDocument.Error!, options.NegativeTtl);
                return KeepOrReject(fetchedAt, parsedDocument.Error!);
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

    // A refresh that fails keeps serving the last known good row for a day, then stops: a client that went away
    // or turned hostile must not live on from the cache.
    private ClientMetadataOutcome KeepOrReject(DateTimeOffset? fetchedAt, string reason) =>
        fetchedAt is { } at && time.GetUtcNow() - at < options.MaxStale ? ClientMetadataOutcome.Ok : ClientMetadataOutcome.Rejected(reason);

    private static string FailureKey(string clientId) => "agent-access:cimd-failure:" + clientId;
}
