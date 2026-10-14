using Hub.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
    public void Store_model_maps_the_four_McpOAuth_tables()
    {
        using var ctx = NewContext();

        var tables = ctx.Model.GetEntityTypes().Select(e => e.GetTableName()).Order().ToArray();

        // Singular PascalCase with the module prefix, like TenantBranding / IntMgr... in the Master DB.
        Assert.Equal(
            ["McpOAuthApplication", "McpOAuthAuthorization", "McpOAuthScope", "McpOAuthToken"],
            tables);
    }

    [Fact]
    public void Keys_and_foreign_keys_are_named_after_the_McpOAuth_tables()
    {
        using var ctx = NewContext();

        foreach (var entity in ctx.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            Assert.Equal($"PK_{table}", entity.FindPrimaryKey()!.GetName());
            Assert.All(entity.GetForeignKeys(), fk => Assert.StartsWith($"FK_{table}_", fk.GetConstraintName()));
            Assert.All(entity.GetIndexes(), ix => Assert.StartsWith($"IX_{table}_", ix.GetDatabaseName()));
        }
    }

    [Fact]
    public void Every_key_and_foreign_key_is_a_Guid()
    {
        using var ctx = NewContext();

        foreach (var entity in ctx.Model.GetEntityTypes())
        {
            var key = Assert.Single(entity.FindPrimaryKey()!.Properties);
            Assert.Equal(typeof(Guid), key.ClrType);

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                Assert.All(foreignKey.Properties, p =>
                    Assert.Equal(typeof(Guid), Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType));
            }
        }
    }

    [Fact]
    public void Every_datetime_column_is_UTC_and_named_with_a_Utc_suffix()
    {
        using var ctx = NewContext();

        var dateColumns = ctx.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties()
                .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(DateTime)
                            || (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(DateTimeOffset))
                .Select(p => (Table: e.GetTableName()!, Column: p.GetColumnName(StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema()))!)))
            .OrderBy(c => c.Table).ThenBy(c => c.Column)
            .ToArray();

        // The convention: a datetime is UTC only if its name ends in Utc.
        Assert.Equal(
            [
                ("McpOAuthAuthorization", "CreationDateUtc"),
                ("McpOAuthToken", "CreationDateUtc"),
                ("McpOAuthToken", "ExpirationDateUtc"),
                ("McpOAuthToken", "RedemptionDateUtc")
            ],
            dateColumns);
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

    private static AgentAccessOpenIddictDbContext NewContext()
    {
        // UseOpenIddict<Guid> returns the non-generic builder, so take Options from the typed one.
        var builder = new DbContextOptionsBuilder<AgentAccessOpenIddictDbContext>().UseSqlServer(UnusedConnection);
        builder.UseOpenIddict<Guid>();
        return new AgentAccessOpenIddictDbContext(builder.Options);
    }

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
