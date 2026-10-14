using Hub.Interfaces;
using Hub.Services;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Serilog;

namespace Hub.Extensions;

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

        IReadOnlyList<AgentAccessClientOptions> clients = settings?.Clients ?? [];
        services.AddSingleton<IAgentAccessClientSeeder>(provider =>
            new AgentAccessClientSeeder(provider.GetRequiredService<IServiceScopeFactory>(), clients));
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
                    .RequireProofKeyForCodeExchange();

                // OAuth 2.1: S256 only. OpenIddict accepts "plain" by default.
                server.Configure(options =>
                    options.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain));

                server.RegisterScopes("mcp", OpenIddictConstants.Scopes.OfflineAccess);

                // Only two fixed scopes and no scope table, so OpenIddict must not look unknown scopes
                // up in the database. The authorization endpoint (phase 1.5) enforces the allow-list.
                server.DisableScopeValidation();

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
