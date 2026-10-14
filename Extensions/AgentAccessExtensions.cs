using Hub.Services;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Hub.Extensions;

public static class AgentAccessExtensions
{
    /// <summary>
    /// Wires OpenIddict into Hub (Agent Access spec v4, D1) with its store on the Master
    /// database. Nothing is reachable until <see cref="UseAgentAccessEndpointGate"/> lets a
    /// request through, and no controller handles /connect/* yet.
    /// </summary>
    public static IServiceCollection AddAgentAccessOAuth(this IServiceCollection services, string masterConnectionString)
    {
        services.AddDbContext<AgentAccessOpenIddictDbContext>(options =>
        {
            options.UseSqlServer(masterConnectionString);
            options.UseOpenIddict();
        });

        services.AddOpenIddict()
            .AddCore(core => core
                .UseEntityFrameworkCore()
                .UseDbContext<AgentAccessOpenIddictDbContext>())
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

                // Placeholder keys so the options validate. Per-pod and unusable across pods,
                // which is fine while the endpoints are dark; phase 1.3 replaces them with
                // the signing and encryption certificates from SSM.
                server.AddEphemeralEncryptionKey()
                    .AddEphemeralSigningKey();

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
