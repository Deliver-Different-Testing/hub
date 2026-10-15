using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Grants;
using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Tests.Helpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Features.AgentAccess.Grants;

// Phase 1.6 and 1.9: with no cookie in play, who is this person right now and may they still have a pass? Every refresh
// and every exchange asks this once. Anything that stops the check is Unknown, never Eligible.
[Collection("EnvironmentVariables")]
public class AgentPassIdentityLoaderTests : IDisposable
{
    private readonly string? _originalCredentials = Environment.GetEnvironmentVariable("SQLCredentials");

    public AgentPassIdentityLoaderTests() =>
        Environment.SetEnvironmentVariable("SQLCredentials", ";User Id=test;Password=test;");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SQLCredentials", _originalCredentials);
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Rig(
        AgentPassIdentityLoader Loader,
        IAuthenticationRepository Master,
        IDespatchRepository Tenant,
        IConnectionStringManager Connection,
        ITenantAgentSettingSource Settings,
        IAgentGrantStore Store);

    private static Rig Build()
    {
        var master = Substitute.For<IAuthenticationRepository>();
        master.GetUserByIdAsync(55).Returns(new User { UserId = 55, Email = "staff@urgent.test", IsNetworkPartner = true, IsCourier = false });
        master.IsUserAssociatedWithTenantAsync(55, 12).Returns(true);
        master.GetTenantConnectionStringAsync(12).Returns("Server=tenant;Database=d;");
        master.GetTenantByIdAsync(12).Returns(new Tenant { TenantId = 12, Code = "urgent", CountryCode = "NZ", TimeZone = "NZST" });

        var tenant = Substitute.For<IDespatchRepository>();
        tenant.FetchAgentContactAsync(9).Returns(Contact());
        tenant.GetAccountsModeAsync().Returns(2);

        var settings = Substitute.For<ITenantAgentSettingSource>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new TenantAgentSettingRow("Live", SwitchOn: true, ForcedOff: false));

        var connection = Substitute.For<IConnectionStringManager>();
        var store = Substitute.For<IAgentGrantStore>();
        return new Rig(new AgentPassIdentityLoader(master, tenant, connection, settings, store), master, tenant, connection, settings, store);
    }

    private static TucClientContact Contact(bool active = true, int? clientType = 4, int? staffId = 7, int? clientId = 3) => new()
    {
        UcctId = 9, UcctClientId = clientId, StaffId = staffId, Active = active, UcctFirstname = "Sam", UcctSurname = "Staff", ContactRoleId = 2,
        UcctClient = clientType is null ? null : new TucClient { UcclId = 3, UcclInternal = true, ClientTypeId = clientType.Value, NpAgentId = 777 }
    };

    private static Task<AgentPassLoad> Load(Rig rig, string userId = "55") => rig.Loader.LoadAsync(12, userId, 9, Ct);

    [Fact]
    public async Task An_eligible_user_is_described_from_the_master_user_the_tenant_and_the_contact()
    {
        var rig = Build();

        var result = await Load(rig);

        Assert.Equal(AgentGrantEligibility.Eligible, result.Eligibility);
        Assert.Equal(
            new AgentPassIdentity("staff@urgent.test", 55, 12, 9, 3, 7, "NZ", "NZST", "urgent", true, true, 2, 4, 777, "Sam", "Staff", 2),
            result.Identity);
        rig.Connection.Received(1).SetConnectionString("Server=tenant;Database=d;;User Id=test;Password=test;");
        await rig.Store.DidNotReceiveWithAnyArgs().SetTenantOffAsync(default, default);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Tenant_staff_and_df_admin_contacts_are_eligible(int clientType)
    {
        var rig = Build();
        rig.Tenant.FetchAgentContactAsync(9).Returns(Contact(clientType: clientType));

        Assert.Equal(AgentGrantEligibility.Eligible, (await Load(rig)).Eligibility);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public async Task A_contact_who_moved_to_a_client_type_that_cannot_connect_is_no_longer_eligible(int clientType)
    {
        var rig = Build();
        rig.Tenant.FetchAgentContactAsync(9).Returns(Contact(clientType: clientType));

        var result = await Load(rig);

        Assert.Equal(AgentGrantEligibility.NoLongerEligible, result.Eligibility);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task A_deactivated_or_missing_contact_or_one_with_no_staff_id_or_client_is_no_longer_eligible()
    {
        foreach (var contact in new TucClientContact?[] { Contact(active: false), null, Contact(staffId: null), Contact(clientId: null), Contact(clientType: null) })
        {
            var rig = Build();
            rig.Tenant.FetchAgentContactAsync(9).Returns(contact);

            Assert.Equal(AgentGrantEligibility.NoLongerEligible, (await Load(rig)).Eligibility);
        }
    }

    [Fact]
    public async Task A_user_removed_from_the_tenant_or_who_became_a_courier_or_vanished_is_no_longer_eligible()
    {
        var removed = Build();
        removed.Master.IsUserAssociatedWithTenantAsync(55, 12).Returns(false);
        var courier = Build();
        courier.Master.GetUserByIdAsync(55).Returns(new User { UserId = 55, IsCourier = true });
        var gone = Build();
        gone.Master.GetUserByIdAsync(55).Returns((User?)null);
        var noTenant = Build();
        noTenant.Master.GetTenantByIdAsync(12).Returns((Tenant?)null);

        foreach (var rig in new[] { removed, courier, gone, noTenant })
        {
            Assert.Equal(AgentGrantEligibility.NoLongerEligible, (await Load(rig)).Eligibility);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public async Task A_user_id_that_is_not_a_number_is_no_longer_eligible(string userId)
    {
        Assert.Equal(AgentGrantEligibility.NoLongerEligible, (await Load(Build(), userId)).Eligibility);
    }

    [Theory]
    [InlineData("Live", true, false, true)]
    [InlineData("Demo", true, false, true)]
    [InlineData("Live", false, false, false)]
    [InlineData("Live", true, true, false)]
    [InlineData("Suspended", true, false, false)]
    [InlineData("Configuring", true, false, false)]
    [InlineData(null, true, false, false)]
    public async Task The_tenant_must_have_agent_access_on_and_be_live_or_demo(string? lifecycle, bool switchOn, bool forcedOff, bool allowed)
    {
        var rig = Build();
        rig.Settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new TenantAgentSettingRow(lifecycle, switchOn, forcedOff));

        var result = await Load(rig);

        Assert.Equal(allowed ? AgentGrantEligibility.Eligible : AgentGrantEligibility.TenantOff, result.Eligibility);
        if (allowed)
        {
            await rig.Store.DidNotReceiveWithAnyArgs().SetTenantOffAsync(default, default);
        }
        else
        {
            await rig.Store.Received(1).SetTenantOffAsync(12, Arg.Any<CancellationToken>());
            await rig.Tenant.DidNotReceiveWithAnyArgs().FetchAgentContactAsync(default);
        }
    }

    [Fact]
    public async Task A_missing_settings_row_is_off_and_unreadable_settings_are_unknown()
    {
        var none = Build();
        none.Settings.GetAsync(Arg.Any<CancellationToken>()).Returns((TenantAgentSettingRow?)null);
        Assert.Equal(AgentGrantEligibility.TenantOff, (await Load(none)).Eligibility);

        var broken = Build();
        broken.Settings.GetAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Invalid column name"));
        Assert.Equal(AgentGrantEligibility.Unknown, (await Load(broken)).Eligibility);
    }

    [Fact]
    public async Task Anything_that_stops_the_check_is_unknown_and_never_returns_an_identity()
    {
        var noConnection = Build();
        noConnection.Master.GetTenantConnectionStringAsync(12).Returns((string?)null);
        var failing = Build();
        failing.Tenant.FetchAgentContactAsync(9).ThrowsAsync(new InvalidOperationException("database down"));

        foreach (var rig in new[] { noConnection, failing })
        {
            var result = await Load(rig);
            Assert.Equal(AgentGrantEligibility.Unknown, result.Eligibility);
            Assert.Null(result.Identity);
        }

        Environment.SetEnvironmentVariable("SQLCredentials", "");
        Assert.Equal(AgentGrantEligibility.Unknown, (await Load(Build())).Eligibility);
    }
}

public class AgentContactRepositoryTests
{
    [Fact]
    public async Task The_agent_contact_comes_with_its_client()
    {
        var context = TestDespatchContextFactory.CreateWithSeedData();
        var repo = new Repository(context, Substitute.For<ITenantService>());

        var contact = await repo.FetchAgentContactAsync(1);

        Assert.NotNull(contact);
        Assert.NotNull(contact!.UcctClient);
        Assert.Null(await repo.FetchAgentContactAsync(999999));
    }
}

public class TenantByIdRepositoryTests
{
    [Fact]
    public async Task A_tenant_is_found_by_id_or_not_at_all()
    {
        var master = TestMasterContextFactory.CreateWithSeedData();
        var repo = new AuthenticationRepository(master);
        var anyTenant = master.Tenants.First();

        Assert.Equal(anyTenant.Code, (await repo.GetTenantByIdAsync(anyTenant.TenantId))!.Code);
        Assert.Null(await repo.GetTenantByIdAsync(-5));
    }
}
