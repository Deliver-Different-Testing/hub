using Hub.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Hub.Services;

namespace Hub.Features.AgentAccess;

/// <summary>One pre-registered public OAuth client, from configuration (AgentAccess:Clients).</summary>
public sealed record AgentAccessClientOptions
{
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Shown to the user on the consent screen.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Company behind the app, shown on the consent screen. Optional.</summary>
    public string? Vendor { get; init; }

    public string[] RedirectUris { get; init; } = [];
}

/// <inheritdoc cref="IAgentAccessClientSeeder"/>
public sealed class AgentAccessClientSeeder(
    IServiceScopeFactory scopes,
    IReadOnlyList<AgentAccessClientOptions> clients,
    TimeProvider? timeProvider = null,
    string? mcpClientSecret = null) : IAgentAccessClientSeeder
{
    /// <summary>Reserved for the confidential token-exchange client (phase 1.9); never a pre-registered AI app.</summary>
    public const string ReservedTokenExchangeClientId = "dfrnt-mcp";

    /// <summary>The RFC 8693 token-exchange grant the MCP server uses to turn an agent pass into an app pass.</summary>
    public const string TokenExchangeGrantType = GrantTypes.TokenExchange;

    private const int MaxClientIdLength = 100; // McpOAuthApplication.ClientId is nvarchar(100)
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private bool _done;
    private DateTimeOffset _retryAtUtc = DateTimeOffset.MinValue;

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (_done) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_done) return;

            if (clients.Count == 0 && string.IsNullOrEmpty(mcpClientSecret))
            {
                _done = true;
                return;
            }

            // After a failure, do not hit the database again on every request.
            if (_time.GetUtcNow() < _retryAtUtc) return;

            try
            {
                await SeedAsync(cancellationToken);
                _done = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _retryAtUtc = _time.GetUtcNow() + RetryAfter;
                Log.Error(ex, "Agent access could not seed its OAuth clients; will retry in {Minutes} minute(s)", RetryAfter.TotalMinutes);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        var desired = new List<OpenIddictApplicationDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var client in clients)
        {
            var problem = Validate(client);
            if (problem is not null)
            {
                Log.Error("Agent access client {ClientId} is not seeded: {Problem}", client.ClientId, problem);
                continue;
            }

            if (!seen.Add(client.ClientId))
            {
                Log.Warning("Agent access client {ClientId} is configured more than once; the first entry wins", client.ClientId);
                continue;
            }

            desired.Add(ToDescriptor(client));
        }

        if (desired.Count == 0 && string.IsNullOrEmpty(mcpClientSecret)) return;

        using var scope = scopes.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var descriptor in desired)
        {
            await UpsertAsync(manager, descriptor, cancellationToken);
        }

        if (!string.IsNullOrEmpty(mcpClientSecret))
        {
            await UpsertTokenExchangeClientAsync(manager, mcpClientSecret, cancellationToken);
        }
    }

    // The one confidential client: the MCP server for this tenant, with one secret per tenant deployment. A changed
    // secret in configuration replaces the stored hash.
    private static async Task UpsertTokenExchangeClientAsync(
        IOpenIddictApplicationManager manager, string secret, CancellationToken cancellationToken)
    {
        var desired = new OpenIddictApplicationDescriptor
        {
            ClientId = ReservedTokenExchangeClientId,
            DisplayName = "DFRNT MCP server",
            ClientType = ClientTypes.Confidential,
            ClientSecret = secret
        };
        desired.Permissions.UnionWith([Permissions.Endpoints.Token, Permissions.GrantTypes.TokenExchange]);

        var existing = await manager.FindByClientIdAsync(ReservedTokenExchangeClientId, cancellationToken);
        if (existing is null)
        {
            await manager.CreateAsync(desired, cancellationToken);
            Log.Information("Agent access token-exchange client created");
            return;
        }

        var current = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(current, existing, cancellationToken);
        if (Same(current, desired) && await manager.ValidateClientSecretAsync(existing, secret, cancellationToken)) return;

        await manager.UpdateAsync(existing, desired, cancellationToken);
        Log.Information("Agent access token-exchange client updated");
    }

    // Creates a missing client, updates a changed one in place, leaves an identical one alone.
    // Never deletes, and never touches a row that is not in the configuration.
    private static async Task UpsertAsync(
        IOpenIddictApplicationManager manager, OpenIddictApplicationDescriptor desired, CancellationToken cancellationToken)
    {
        var existing = await manager.FindByClientIdAsync(desired.ClientId!, cancellationToken);
        if (existing is null)
        {
            await manager.CreateAsync(desired, cancellationToken);
            Log.Information("Agent access client {ClientId} created", desired.ClientId);
            return;
        }

        var current = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(current, existing, cancellationToken);
        if (Same(current, desired)) return;

        await manager.UpdateAsync(existing, desired, cancellationToken);
        Log.Information("Agent access client {ClientId} updated", desired.ClientId);
    }

    private static bool Same(OpenIddictApplicationDescriptor current, OpenIddictApplicationDescriptor desired) =>
        current.ClientType == desired.ClientType
        && current.ConsentType == desired.ConsentType
        && current.DisplayName == desired.DisplayName
        && current.RedirectUris.SetEquals(desired.RedirectUris)
        && current.PostLogoutRedirectUris.SetEquals(desired.PostLogoutRedirectUris)
        && current.Permissions.SetEquals(desired.Permissions)
        && current.Requirements.SetEquals(desired.Requirements);

    // A public client (no secret) using authorization code with PKCE, explicit consent, and refresh.
    internal static OpenIddictApplicationDescriptor ToDescriptor(AgentAccessClientOptions client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            DisplayName = client.DisplayName,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit
        };

        foreach (var uri in client.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }

        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
            Permissions.Prefixes.Scope + "mcp",
            Permissions.Prefixes.Scope + Scopes.OfflineAccess
        ]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

        return descriptor;
    }

    private static string? Validate(AgentAccessClientOptions client)
    {
        if (string.IsNullOrWhiteSpace(client.ClientId)) return "the client id is required";
        if (client.ClientId.Length > MaxClientIdLength) return $"the client id is longer than {MaxClientIdLength} characters";

        // URL-shaped ids belong to client-metadata documents (CIMD), which Hub fetches and caches itself.
        if (client.ClientId.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || client.ClientId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "a pre-registered client id must not be a URL";
        }

        if (string.Equals(client.ClientId, ReservedTokenExchangeClientId, StringComparison.Ordinal))
        {
            return $"{ReservedTokenExchangeClientId} is reserved for the token-exchange client";
        }

        if (string.IsNullOrWhiteSpace(client.DisplayName)) return "the display name is required (it is shown on the consent screen)";
        if (client.RedirectUris is not { Length: > 0 }) return "at least one redirect URI is required";

        foreach (var redirect in client.RedirectUris)
        {
            if (!Uri.TryCreate(redirect, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                return "every redirect URI must be an absolute https URI without a fragment";
            }
        }

        return null;
    }
}
