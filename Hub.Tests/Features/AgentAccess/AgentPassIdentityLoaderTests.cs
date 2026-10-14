using Hub.Features.AgentAccess;
using Hub.Interfaces;
using Hub.Models;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Tests.Helpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Hub.Tests.Features.AgentAccess;

public class AgentPassIdentityLoaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Rig(
        AgentPassIdentityLoader Loader,
        IAgentGrantEligibilityChecker Eligibility,
        IAuthenticationRepository Master,
        IDespatchRepository Tenant);

    private static Rig Build(AgentGrantEligibility eligibility = AgentGrantEligibility.Eligible)
    {
        var checker = Substitute.For<IAgentGrantEligibilityChecker>();
        checker.CheckAsync(12, "55", 9, Arg.Any<CancellationToken>()).Returns(eligibility);
        var master = Substitute.For<IAuthenticationRepository>();
        master.GetUserByIdAsync(55).Returns(new User { UserId = 55, Email = "staff@urgent.test", IsNetworkPartner = true });
        master.GetTenantByIdAsync(12).Returns(new Tenant { TenantId = 12, Code = "urgent", CountryCode = "NZ", TimeZone = "NZST" });
        var tenant = Substitute.For<IDespatchRepository>();
        tenant.FetchAgentContactAsync(9).Returns(new TucClientContact
        {
            UcctId = 9, UcctClientId = 3, StaffId = 7, UcctFirstname = "Sam", UcctSurname = "Staff", ContactRoleId = 2,
            UcctClient = new TucClient { UcclId = 3, UcclInternal = true, ClientTypeId = 4, NpAgentId = 777 }
        });
        tenant.GetAccountsModeAsync().Returns(2);
        return new Rig(new AgentPassIdentityLoader(checker, master, tenant), checker, master, tenant);
    }

    [Fact]
    public async Task An_eligible_user_is_described_from_the_master_user_the_tenant_and_the_contact()
    {
        var result = await Build().Loader.LoadAsync(12, "55", 9, Ct);

        Assert.Equal(AgentGrantEligibility.Eligible, result.Eligibility);
        Assert.Equal(
            new AgentPassIdentity("staff@urgent.test", 55, 12, 9, 3, 7, "NZ", "NZST", "urgent", true, true, 2, 4, 777, "Sam", "Staff", 2),
            result.Identity);
    }

    [Theory]
    [InlineData(AgentGrantEligibility.NoLongerEligible)]
    [InlineData(AgentGrantEligibility.Unknown)]
    public async Task Nobody_is_loaded_when_they_are_not_eligible(AgentGrantEligibility verdict)
    {
        var rig = Build(verdict);

        var result = await rig.Loader.LoadAsync(12, "55", 9, Ct);

        Assert.Equal(verdict, result.Eligibility);
        Assert.Null(result.Identity);
        await rig.Tenant.DidNotReceiveWithAnyArgs().FetchAgentContactAsync(default);
    }

    [Fact]
    public async Task A_contact_without_a_staff_id_or_client_cannot_have_a_pass()
    {
        var noStaff = Build();
        noStaff.Tenant.FetchAgentContactAsync(9).Returns(new TucClientContact { UcctId = 9, UcctClientId = 3, StaffId = null, UcctClient = new TucClient { ClientTypeId = 4 } });
        var noClient = Build();
        noClient.Tenant.FetchAgentContactAsync(9).Returns(new TucClientContact { UcctId = 9, UcctClientId = null, StaffId = 7 });

        Assert.Equal(AgentGrantEligibility.NoLongerEligible, (await noStaff.Loader.LoadAsync(12, "55", 9, Ct)).Eligibility);
        Assert.Equal(AgentGrantEligibility.NoLongerEligible, (await noClient.Loader.LoadAsync(12, "55", 9, Ct)).Eligibility);
    }

    [Fact]
    public async Task A_missing_tenant_or_user_is_no_longer_eligible()
    {
        var noTenant = Build();
        noTenant.Master.GetTenantByIdAsync(12).Returns((Tenant?)null);
        var noUser = Build();
        noUser.Master.GetUserByIdAsync(55).Returns((User?)null);

        Assert.Equal(AgentGrantEligibility.NoLongerEligible, (await noTenant.Loader.LoadAsync(12, "55", 9, Ct)).Eligibility);
        Assert.Equal(AgentGrantEligibility.NoLongerEligible, (await noUser.Loader.LoadAsync(12, "55", 9, Ct)).Eligibility);
    }

    [Fact]
    public async Task A_database_error_is_unknown_not_a_crash_or_a_pass()
    {
        var rig = Build();
        rig.Tenant.FetchAgentContactAsync(9).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await rig.Loader.LoadAsync(12, "55", 9, Ct);

        Assert.Equal(AgentGrantEligibility.Unknown, result.Eligibility);
        Assert.Null(result.Identity);
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
