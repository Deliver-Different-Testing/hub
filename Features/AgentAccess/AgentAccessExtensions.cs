using Hub.Interfaces;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using Serilog;
using Hub.Extensions;

namespace Hub.Features.AgentAccess;

public static class AgentAccessExtensions
{
    /// <summary>
    /// Wires OpenIddict into Hub (Agent Access spec v4, D1) with its store on the Master
    /// database. Nothing is reachable until <see cref="UseAgentAccessEndpointGate"/> lets a
    /// request through, and no controller handles /connect/* yet.
    /// </summary>
    public static IServiceCollection AddAgentAccessOAuth(
        this IServiceCollection services, string masterConnectionString, AgentAccessOAuthSettings? settings = null)
    {
        var bootstrap = AgentAccessKeyBootstrap.Resolve(settings);
        services.AddSingleton<IAgentAccessKeyState>(bootstrap.State);

        var mcpResource = AgentAccessResource.Resolve(settings?.Issuer, settings?.McpResource);
        services.AddSingleton(new AgentAccessMcpResource(mcpResource));
        if (mcpResource is null)
        {
            Log.Warning("Agent access has no MCP resource URL (set AgentAccessIssuer to https://hub.<tenant host> or AgentAccessMcpResource); every authorization request will be rejected");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<OpenIddict.Server.OpenIddictServerOptions>()
            .Configure<TimeProvider>((options, time) => options.TimeProvider = time);
        services.TryAddSingleton<IAgentGrantStore, AgentGrantStore>();
        services.TryAddSingleton<IAgentGrantAdmin, AgentGrantAdmin>();
        services.TryAddScoped<IAgentPassIdentityLoader, AgentPassIdentityLoader>();
        services.TryAddSingleton<IAgentSubjectTokenValidator, AgentSubjectTokenValidator>();

        var metadataOptions = ClientMetadataOptions.Create(settings?.ClientMetadataHosts);
        services.AddSingleton(metadataOptions);
        services.AddSingleton<ClientMetadataFailures>();
        services.AddScoped<IClientMetadataService, ClientMetadataService>();
        services.AddHttpClient<IClientMetadataFetcher, ClientMetadataFetcher>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = SsrfSafeConnect.ConnectAsync
            });
        if (metadataOptions.Enabled)
        {
            Log.Information("Agent access accepts client ID metadata documents from {Hosts}", string.Join(", ", metadataOptions.AllowedHosts));
        }

        IReadOnlyList<AgentAccessClientOptions> clients = settings?.Clients ?? [];
        services.AddSingleton(new AgentAccessClientCatalog(clients));
        services.AddSingleton<IAgentAccessClientSeeder>(provider =>
            new AgentAccessClientSeeder(
                provider.GetRequiredService<IServiceScopeFactory>(), clients, mcpClientSecret: settings?.McpClientSecret));
        if (bootstrap.State.Persistent)
        {
            Log.Information("Agent access keys are persistent (signing kid {SigningKeyId})", bootstrap.State.SigningKeyId);
        }
        else
        {
            Log.Warning("Agent access keys are not persistent, so the agent access gate stays closed: {Reason}", bootstrap.State.Reason);
        }

        services.AddDbContext<AgentAccessOpenIddictDbContext>(options =>
        {
            options.UseSqlServer(masterConnectionString);
            options.UseOpenIddict<Guid>();
        });

        services.AddOpenIddict()
            .AddCore(core => core
                .UseEntityFrameworkCore()
                .UseDbContext<AgentAccessOpenIddictDbContext>()
                .ReplaceDefaultEntities<Guid>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetConfigurationEndpointUris("/.well-known/openid-configuration")
                    .SetJsonWebKeySetEndpointUris("/.well-known/jwks");

                server.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowTokenExchangeFlow()
                    .RequireProofKeyForCodeExchange();

                // OAuth 2.1: S256 only. OpenIddict accepts "plain" by default.
                server.Configure(options =>
                    options.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain));

                server.RegisterScopes("mcp", OpenIddictConstants.Scopes.OfflineAccess);

                // Only two fixed scopes and no scope table, so OpenIddict must not look unknown scopes
                // up in the database. The authorization endpoint (phase 1.5) enforces the allow-list.
                server.DisableScopeValidation();

                // The authorization endpoint accepts exactly one resource, the MCP server of this tenant, and
                // enforces it itself, so OpenIddict neither needs a registered list nor per-client permissions.
                server.DisableResourceValidation()
                    .IgnoreResourcePermissions();

                // Persistent RSA keys from SSM when configured (RS256). Otherwise per-pod
                // ephemeral keys so the options validate; the gate stays closed in that case.
                if (bootstrap.Keys is { } keys)
                {
                    // OpenIddict signs with the first credential; the previous key is only published.
                    server.AddSigningKey(keys.Signing);
                    if (keys.PreviousSigning is { } previous)
                    {
                        server.AddSigningKey(previous);
                    }

                    server.AddEncryptionKey(keys.Encryption);
                }
                else
                {
                    server.AddEphemeralEncryptionKey()
                        .AddEphemeralSigningKey();
                }

                if (bootstrap.Issuer is { } issuer)
                {
                    server.SetIssuer(issuer);
                }

                // Spec v4 Group 1: signed JWT access tokens (20 min), reference refresh tokens
                // (90 days) with a 30 s reuse leeway.
                server.DisableAccessTokenEncryption()
                    .SetAccessTokenLifetime(TimeSpan.FromMinutes(20))
                    .SetRefreshTokenLifetime(TimeSpan.FromDays(90))
                    .SetRefreshTokenReuseLeeway(TimeSpan.FromSeconds(30))
                    .UseReferenceRefreshTokens();

                server.AddEventHandler(AgentAccessClientMetadataHandler.Descriptor);
                server.AddEventHandler(AgentAccessClientMetadataDiscoveryHandler.Descriptor);

                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            });

        return services;
    }

    /// <summary>Answers 404 for the OAuth endpoints unless the agent access gate is open. Place before UseAuthentication.</summary>
    public static IApplicationBuilder UseAgentAccessEndpointGate(this IApplicationBuilder app) =>
        app.UseMiddleware<AgentAccessEndpointGate>();
}
