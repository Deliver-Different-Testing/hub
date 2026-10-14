using Hub.Features.AgentAccess;
using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Tests.Helpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Features.AgentAccess;

[Collection("EnvironmentVariables")]
public class AgentGrantEligibilityTests : IDisposable
{
    private readonly string? _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials");

    public AgentGrantEligibilityTests() =>
        Environment.SetEnvironmentVariable("SQLCredentials", ";User Id=test;Password=test;");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Fixture(
        AgentGrantEligibilityChecker Checker,
        IAuthenticationRepository Master,
        IDespatchRepository Tenant,
        IConnectionStringManager Connection);

    private static Fixture Build(
        User? masterUser = null,
        bool associated = true,
        string? connection = "Server=tenant;Database=d;",
        AgentContactStatus? contact = null)
    {
        var master = Substitute.For<IAuthenticationRepository>();
        master.GetUserByIdAsync(55).Returns(masterUser ?? new User { UserId = 55, IsCourier = false });
        master.IsUserAssociatedWithTenantAsync(55, 12).Returns(associated);
        master.GetTenantConnectionStringAsync(12).Returns(connection);

        var tenant = Substitute.For<IDespatchRepository>();
        tenant.FetchContactStatusAsync(9).Returns(contact ?? new AgentContactStatus(Active: true, ClientTypeId: 4));
        var conn = Substitute.For<IConnectionStringManager>();
        return new Fixture(new AgentGrantEligibilityChecker(master, tenant, conn), master, tenant, conn);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task An_active_tenant_or_df_admin_contact_is_eligible(int clientType)
    {
        var f = Build(contact: new AgentContactStatus(true, clientType));

        Assert.Equal(AgentGrantEligibility.Eligible, await f.Checker.CheckAsync(12, "55", 9, Ct));
        f.Connection.Received(1).SetConnectionString("Server=tenant;Database=d;;User Id=test;Password=test;");
    }

    [Fact]
    public async Task A_deactivated_contact_is_no_longer_eligible()
    {
        var f = Build(contact: new AgentContactStatus(false, 4));

        Assert.Equal(AgentGrantEligibility.NoLongerEligible, await f.Checker.CheckAsync(12, "55", 9, Ct));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(null)]
    public async Task A_contact_who_moved_to_a_client_type_that_cannot_connect_is_no_longer_eligible(int? clientType)
    {
        var f = Build(contact: new AgentContactStatus(true, clientType));

        Assert.Equal(AgentGrantEligibility.NoLongerEligible, await f.Checker.CheckAsync(12, "55", 9, Ct));
    }

    [Fact]
    public async Task A_contact_that_no_longer_exists_is_no_longer_eligible()
    {
        var f = Build();
        f.Tenant.FetchContactStatusAsync(9).Returns((AgentContactStatus?)null);

        Assert.Equal(AgentGrantEligibility.NoLongerEligible, await f.Checker.CheckAsync(12, "55", 9, Ct));
    }

    [Fact]
    public async Task A_user_removed_from_the_tenant_or_who_became_a_courier_is_no_longer_eligible()
    {
        Assert.Equal(AgentGrantEligibility.NoLongerEligible, await Build(associated: false).Checker.CheckAsync(12, "55", 9, Ct));
        Assert.Equal(AgentGrantEligibility.NoLongerEligible, await Build(masterUser: new User { UserId = 55, IsCourier = true }).Checker.CheckAsync(12, "55", 9, Ct));

        var gone = Build();
        gone.Master.GetUserByIdAsync(55).Returns((User?)null);
        Assert.Equal(AgentGrantEligibility.NoLongerEligible, await gone.Checker.CheckAsync(12, "55", 9, Ct));
    }

    [Fact]
    public async Task A_user_id_that_is_not_a_number_is_no_longer_eligible()
    {
        Assert.Equal(AgentGrantEligibility.NoLongerEligible, await Build().Checker.CheckAsync(12, "abc", 9, Ct));
    }

    [Fact]
    public async Task Anything_that_stops_the_check_is_unknown_not_eligible()
    {
        Assert.Equal(AgentGrantEligibility.Unknown, await Build(connection: null).Checker.CheckAsync(12, "55", 9, Ct));

        var failing = Build();
        failing.Tenant.FetchContactStatusAsync(9).ThrowsAsync(new InvalidOperationException("database down"));
        Assert.Equal(AgentGrantEligibility.Unknown, await failing.Checker.CheckAsync(12, "55", 9, Ct));

        Environment.SetEnvironmentVariable("SQLCredentials", "");
        Assert.Equal(AgentGrantEligibility.Unknown, await Build().Checker.CheckAsync(12, "55", 9, Ct));
    }
}

public class AgentContactStatusRepositoryTests
{
    private static Repository Repo(out DynamicDespatchDbContext context)
    {
        context = TestDespatchContextFactory.CreateWithSeedData();
        var tenantService = Substitute.For<ITenantService>();
        return new Repository(context, tenantService);
    }

    private static TucClient Client(int id, int clientType) => new()
    {
        UcclId = id, UcclName = $"C{id}", UcclLegalName = $"C{id} Ltd", UcclCode = $"C{id}", UcclActive = true,
        ClientTypeId = clientType, Smsname = "s", CreatedBy = "t", LastModifiedBy = "t",
        Created = DateTime.Now, LastModified = DateTime.Now
    };

    private static TucClientContact Contact(int id, int clientId, bool active) => new()
    {
        UcctId = id, UcctClientId = clientId, UcctFirstname = "A", UcctSurname = "B", UcctEmail = $"{id}@t.test",
        UserName = $"{id}@t.test", Active = active, CreatedBy = "t", LastModifiedBy = "t",
        Created = DateTime.Now, LastModified = DateTime.Now
    };

    [Fact]
    public async Task Reads_whether_the_contact_is_active_and_their_client_type()
    {
        var repo = Repo(out var context);
        context.TucClients.AddRange(Client(40, 4), Client(50, 5));
        context.TucClientContacts.AddRange(Contact(400, 40, true), Contact(401, 40, false), Contact(500, 50, true));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new AgentContactStatus(true, 4), await repo.FetchContactStatusAsync(400));
        Assert.Equal(new AgentContactStatus(false, 4), await repo.FetchContactStatusAsync(401));
        Assert.Equal(new AgentContactStatus(true, 5), await repo.FetchContactStatusAsync(500));
        Assert.Null(await repo.FetchContactStatusAsync(999999));
    }
}
