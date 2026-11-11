using Hub.Features.AgentAccess.ClientMetadata;
using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Grants;
using Hub.Features.AgentAccess.Keys;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using Serilog;

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
        services.AddSingleton(new AgentAccessIssuer(bootstrap.Issuer));

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

        var mcpAssertion = AgentAccessMcpAssertionOptions.Resolve(settings?.ClusterIssuer, settings?.McpServiceAccounts);
        services.AddSingleton(mcpAssertion);
        services.AddSingleton(new AgentAccessMcpExchange(
            SecretConfigured: !string.IsNullOrEmpty(settings?.McpClientSecret), AssertionConfigured: mcpAssertion.Enabled));
        services.AddSingleton<IAgentAccessClusterJwks, AgentAccessClusterJwks>();
        services.TryAddSingleton<IAgentAccessMcpAssertionValidator, AgentAccessMcpAssertionValidator>();
        services.AddHttpClient(AgentAccessClusterJwks.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        if (mcpAssertion.Enabled)
        {
            Log.Information("Agent access accepts the MCP server's service-account token from {ClusterIssuer} for {ServiceAccounts}",
                mcpAssertion.ClusterIssuer, string.Join(", ", mcpAssertion.ServiceAccounts));
        }

        IReadOnlyList<AgentAccessClientOptions> clients = settings?.Clients ?? [];
        services.AddSingleton(new AgentAccessClientCatalog(clients));
        services.AddSingleton<AgentAccessConnectTickets>();
        services.AddSingleton<IAgentAccessClientSeeder>(provider =>
            new AgentAccessClientSeeder(
                provider.GetRequiredService<IServiceScopeFactory>(), clients,
                mcpClientSecret: settings?.McpClientSecret, mcpAssertion: mcpAssertion.Enabled));
        if (bootstrap.State.Persistent)
        {
            Log.Information(
                "Agent access keys are persistent (signing kid {SigningKeyId}, encryption kid {EncryptionKeyId})",
                bootstrap.State.SigningKeyId, bootstrap.State.EncryptionKeyId);
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
                    // RFC 8414's address as well as OIDC's: the MCP authorization spec has clients try both.
                    .SetConfigurationEndpointUris("/.well-known/oauth-authorization-server", "/.well-known/openid-configuration")
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
                    // In loader order: OpenIddict signs and encrypts with the first credential; the rest are only
                    // published (signing) or accepted (encryption).
                    foreach (var signing in keys.SigningKeys)
                    {
                        server.AddSigningKey(signing);
                    }

                    foreach (var encryption in keys.EncryptionKeys)
                    {
                        server.AddEncryptionKey(encryption);
                    }
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
                server.AddEventHandler(AgentAccessMcpAssertionHandler.Descriptor);

                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            });

        return services;
    }

    /// <summary>
    /// Answers 404 for the OAuth endpoints while agent access is dark, and says why when the tenant has it switched off.
    /// Place before UseAuthentication.
    /// </summary>
    public static void UseAgentAccessEndpointGate(this IApplicationBuilder app)
    {
        app.UseMiddleware<AgentAccessEndpointGate>();
    }

    /// <summary>The SPA's pages for connecting an AI app (consent, organisation switch, unavailable) live under this path.</summary>
    public const string PagePathPrefix = "/agent-access";

    /// <summary>
    /// The connect pages are SPA routes, so the framing and caching rules the Razor consent page used to send go on
    /// the index.html response instead (RFC 9700 section 4.16; a meta tag or client-side check would do nothing).
    /// Set as the response starts, so they replace the static-file Cache-Control. Place before UseStaticFiles.
    /// </summary>
    public static void UseAgentAccessPageHeaders(this IApplicationBuilder app)
    {
        app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(PagePathPrefix))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["X-Frame-Options"] = "DENY";
                    context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.Headers.Remove("Pragma");
                    context.Response.Headers.Remove("Expires");
                    return Task.CompletedTask;
                });
            }

            return next(context);
        });
    }
}

/// <param name="ConnectionString">The connection string the OAuth store will use.</param>
/// <param name="Dedicated">True when it is the least-privilege McpOAuthStore login, false when it fell back to MasterSQLConnection.</param>
/// <param name="Description">A line safe to log: says which one is in use and never contains a connection string.</param>
public sealed record AgentAccessOAuthStoreConnection(string ConnectionString, bool Dedicated, string Description);

/// <summary>
/// Chooses the connection string for the OAuth store. The dedicated login can reach only the three
/// McpOAuth tables, so a flaw in the OAuth store cannot touch tenants, users or connection strings in
/// the Master database. Falling back to MasterSQLConnection keeps Hub working until the login exists.
/// </summary>
public static class AgentAccessOAuthStore
{
    public static AgentAccessOAuthStoreConnection ResolveConnectionString(
        string masterConnectionString, string? dedicatedConnectionString)
    {
        return string.IsNullOrWhiteSpace(dedicatedConnectionString)
            ? new AgentAccessOAuthStoreConnection(
                masterConnectionString,
                Dedicated: false,
                "AgentAccessOAuthSQLConnection is not set; the OAuth store uses MasterSQLConnection, which can reach far more than it needs")
            : new AgentAccessOAuthStoreConnection(
                dedicatedConnectionString,
                Dedicated: true,
                "the OAuth store uses its dedicated least-privilege login");
    }
}
