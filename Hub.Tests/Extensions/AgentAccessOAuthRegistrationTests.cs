using Hub.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Hub.Tests.Extensions;

// Phase 1.2: OpenIddict is wired into Hub with its store on the Master database. These
// tests pin the shape of that wiring; the endpoints themselves stay behind the agent
// access gate (AgentAccessEndpointGate).
public class AgentAccessOAuthRegistrationTests
{
    private const string UnusedConnection = "Server=unused;Database=unused;";

    [Fact]
    public void Store_model_maps_the_four_OpenIddict_tables()
    {
        using var ctx = new AgentAccessOpenIddictDbContext(
            new DbContextOptionsBuilder<AgentAccessOpenIddictDbContext>()
                .UseSqlServer(UnusedConnection)
                .UseOpenIddict()
                .Options);

        var tables = ctx.Model.GetEntityTypes().Select(e => e.GetTableName()).Order().ToArray();

        Assert.Equal(
            ["OpenIddictApplications", "OpenIddictAuthorizations", "OpenIddictScopes", "OpenIddictTokens"],
            tables);
    }

    [Fact]
    public void Server_exposes_only_the_endpoints_the_gate_covers()
    {
        var options = ServerOptions();

        Assert.Contains(options.AuthorizationEndpointUris, u => u.OriginalString == "/connect/authorize");
        Assert.Contains(options.TokenEndpointUris, u => u.OriginalString == "/connect/token");
        Assert.Contains(options.ConfigurationEndpointUris, u => u.OriginalString == "/.well-known/openid-configuration");
        Assert.Contains(options.JsonWebKeySetEndpointUris, u => u.OriginalString == "/.well-known/jwks");
    }

    [Fact]
    public void Only_authorization_code_and_refresh_flows_are_allowed()
    {
        var grants = ServerOptions().GrantTypes;

        Assert.Contains(OpenIddictConstants.GrantTypes.AuthorizationCode, grants);
        Assert.Contains(OpenIddictConstants.GrantTypes.RefreshToken, grants);
        Assert.DoesNotContain(OpenIddictConstants.GrantTypes.ClientCredentials, grants);
        Assert.DoesNotContain(OpenIddictConstants.GrantTypes.Password, grants);
        Assert.DoesNotContain(OpenIddictConstants.GrantTypes.Implicit, grants);
    }

    [Fact]
    public void Pkce_is_S256_only()
    {
        var methods = ServerOptions().CodeChallengeMethods;

        Assert.Contains(OpenIddictConstants.CodeChallengeMethods.Sha256, methods);
        Assert.DoesNotContain(OpenIddictConstants.CodeChallengeMethods.Plain, methods);
    }

    [Fact]
    public void Registers_the_mcp_and_offline_access_scopes()
    {
        var scopes = ServerOptions().Scopes;

        Assert.Contains("mcp", scopes);
        Assert.Contains(OpenIddictConstants.Scopes.OfflineAccess, scopes);
    }

    [Fact]
    public void Server_has_keys_so_options_validate()
    {
        // Ephemeral for now; phase 1.3 replaces them with certificates from SSM. Without
        // any key OpenIddict throws on the first request that reaches its handler.
        var options = ServerOptions();

        Assert.NotEmpty(options.SigningCredentials);
        Assert.NotEmpty(options.EncryptionCredentials);
    }

    [Fact]
    public void Managers_resolve_from_the_container()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IOpenIddictApplicationManager>());
        Assert.NotNull(scope.ServiceProvider.GetService<IOpenIddictAuthorizationManager>());
    }

    // ---- helpers ----------------------------------------------------------

    private static OpenIddictServerOptions ServerOptions()
    {
        using var provider = Provider();
        return provider.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentAccessOAuth(UnusedConnection);
        return services.BuildServiceProvider();
    }
}
