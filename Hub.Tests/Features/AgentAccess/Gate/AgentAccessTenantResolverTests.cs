using Hub.Features.AgentAccess.Gate;
using Hub.Interfaces;
using Hub.Models.Master;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Gate;

// Claude's first calls to Hub are anonymous, so the tenant cannot come from the cookie. It comes from the configured
// issuer host and the Master DB, and the request's tenant database is pointed at it so the gate can read its settings.
public class AgentAccessTenantResolverTests
{
    private const string Credentials = ";User=agent;Password=secret;";

    [Theory]
    [InlineData("https://hub.urgent.deliverdifferent.com", "urgent")]
    [InlineData("https://hub.urgent.staging.deliverdifferent.com", "urgent")]
    [InlineData("https://hub.staging.deliverdifferent.com", "dfrnt")]
    public async Task The_tenant_comes_from_the_issuer_host(string issuer, string code)
    {
        var master = Master(new Tenant { TenantId = 12, Code = code, Name = "Urgent Couriers", Dbconnection = "Server=db;Database=t12;" });
        var connection = Substitute.For<IConnectionStringManager>();

        var tenant = await Resolver(issuer, master, connection).ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new AgentAccessTenant(12, code, "Urgent Couriers"), tenant);
        await master.Received(1).GetTenantByCodeAsync(code);
        connection.Received(1).SetConnectionString("Server=db;Database=t12;" + Credentials);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://hub.local.deliverdifferent.com")]
    [InlineData("https://hub.dev.deliverdifferent.com")]
    [InlineData("https://hub.urgent.example.test")]
    public async Task No_tenant_when_the_issuer_names_none(string? issuer)
    {
        var master = Master(null);
        var connection = Substitute.For<IConnectionStringManager>();

        Assert.Null(await Resolver(issuer, master, connection).ResolveAsync(TestContext.Current.CancellationToken));
        await master.DidNotReceive().GetTenantByCodeAsync(Arg.Any<string>());
        connection.DidNotReceive().SetConnectionString(Arg.Any<string>());
    }

    [Fact]
    public async Task No_tenant_when_the_master_db_has_no_such_code()
    {
        var connection = Substitute.For<IConnectionStringManager>();

        var tenant = await Resolver("https://hub.urgent.deliverdifferent.com", Master(null), connection)
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Null(tenant);
        connection.DidNotReceive().SetConnectionString(Arg.Any<string>());
    }

    [Fact]
    public async Task No_tenant_without_its_database_or_the_sql_credentials()
    {
        var noDatabase = Master(new Tenant { TenantId = 12, Code = "urgent", Name = "Urgent", Dbconnection = "" });
        var withDatabase = Master(new Tenant { TenantId = 12, Code = "urgent", Name = "Urgent", Dbconnection = "Server=db;" });
        var connection = Substitute.For<IConnectionStringManager>();

        Assert.Null(await Resolver("https://hub.urgent.deliverdifferent.com", noDatabase, connection).ResolveAsync(TestContext.Current.CancellationToken));
        Assert.Null(await Resolver("https://hub.urgent.deliverdifferent.com", withDatabase, connection, credentials: null).ResolveAsync(TestContext.Current.CancellationToken));
        connection.DidNotReceive().SetConnectionString(Arg.Any<string>());
    }

    [Fact]
    public async Task The_tenant_is_cached_but_the_connection_is_set_on_every_request()
    {
        var master = Master(new Tenant { TenantId = 12, Code = "urgent", Name = "Urgent", Dbconnection = "Server=db;" });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var first = Substitute.For<IConnectionStringManager>();
        var second = Substitute.For<IConnectionStringManager>();

        await Resolver("https://hub.urgent.deliverdifferent.com", master, first, cache).ResolveAsync(TestContext.Current.CancellationToken);
        var tenant = await Resolver("https://hub.urgent.deliverdifferent.com", master, second, cache).ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(12, tenant!.TenantId);
        await master.Received(1).GetTenantByCodeAsync("urgent");
        second.Received(1).SetConnectionString("Server=db;" + Credentials);
    }

    [Fact]
    public async Task A_missing_tenant_is_cached_too()
    {
        var master = Master(null);
        var cache = new MemoryCache(new MemoryCacheOptions());

        await Resolver("https://hub.urgent.deliverdifferent.com", master, cache: cache).ResolveAsync(TestContext.Current.CancellationToken);
        await Resolver("https://hub.urgent.deliverdifferent.com", master, cache: cache).ResolveAsync(TestContext.Current.CancellationToken);

        await master.Received(1).GetTenantByCodeAsync("urgent");
    }

    [Fact]
    public async Task A_failing_master_db_is_not_cached()
    {
        var master = Substitute.For<IAuthenticationRepository>();
        master.GetTenantByCodeAsync(Arg.Any<string>()).Returns<Tenant?>(_ => throw new InvalidOperationException("down"));
        var cache = new MemoryCache(new MemoryCacheOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Resolver("https://hub.urgent.deliverdifferent.com", master, cache: cache).ResolveAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Resolver("https://hub.urgent.deliverdifferent.com", master, cache: cache).ResolveAsync(TestContext.Current.CancellationToken));

        await master.Received(2).GetTenantByCodeAsync("urgent");
    }

    private static IAuthenticationRepository Master(Tenant? tenant)
    {
        var master = Substitute.For<IAuthenticationRepository>();
        master.GetTenantByCodeAsync(Arg.Any<string>()).Returns(tenant);
        return master;
    }

    private static AgentAccessTenantResolver Resolver(
        string? issuer,
        IAuthenticationRepository master,
        IConnectionStringManager? connection = null,
        IMemoryCache? cache = null,
        string? credentials = Credentials) =>
        new(
            new AgentAccessIssuer(issuer is null ? null : new Uri(issuer)),
            master,
            connection ?? Substitute.For<IConnectionStringManager>(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            () => credentials);
}
