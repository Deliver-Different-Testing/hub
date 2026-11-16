using Hub.Interfaces;
using Hub.Models;
using Hub.Repositories;
using Hub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Hub.Tests.Repositories;

public class RepositoryTests
{
    private static (Repository repo, DynamicDespatchDbContext context) CreateRepo()
    {
        var context = TestDespatchContextFactory.CreateWithSeedData();
        var tenantService = Substitute.For<ITenantService>();
        tenantService.GetCurrentTenantTimeAsync(Arg.Any<int>()).Returns(DateTime.UtcNow);
        var repo = new Repository(context, tenantService);
        return (repo, context);
    }

    // FetchUserByUsername tests
    [Fact]
    public async Task FetchUserByUsername_ActiveUser_ReturnsUser()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsernameAsync("john@test.com");

        Assert.NotNull(user);
        Assert.Equal("John", user.UcctFirstname);
    }

    // A tenant staff contact (client type 4 = Tenant) with the given branch rows. The seed data has none.
    private static async Task<int> AddStaffContactAsync(DynamicDespatchDbContext context, params int[] branchIds)
    {
        context.TucClients.Add(new TucClient
        {
            UcclId = 50, UcclName = "Tenant", UcclLegalName = "Tenant Ltd", UcclCode = "TEN", UcclInternal = true, UcclActive = true,
            UcclGroupId = 0, ClientTypeId = 4, Smsname = "SMS",
            Created = DateTime.Now, CreatedBy = "test", LastModified = DateTime.Now, LastModifiedBy = "test"
        });
        context.TucClientContacts.Add(new TucClientContact
        {
            UcctId = 50, UcctClientId = 50, UcctFirstname = "Tenant", UcctSurname = "Staff", UserName = "tenant.staff@test.com",
            UcctEmail = "tenant.staff@test.com", Active = true, HasEmail = true, ValidatedEmail = true, StaffId = 50,
            Created = DateTime.Now, CreatedBy = "test", LastModified = DateTime.Now, LastModifiedBy = "test"
        });
        context.TblContactBranches.AddRange(branchIds.Select(b => new TblContactBranch { ClientContactId = 50, BranchId = b }));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return 50;
    }

    [Fact]
    public async Task FetchUserByUsername_LoadsTheContactsBranchScope_Ascending()
    {
        var (repo, context) = CreateRepo();
        await AddStaffContactAsync(context, 2, 1);

        var user = await repo.FetchUserByUsernameAsync("tenant.staff@test.com");

        Assert.Equal([1, 2], user!.BranchIds);
    }

    [Fact]
    public async Task FetchUserByUsername_DoesNotReadTheBranchTable_ForANetworkPartnerContact()
    {
        // np@test.com sits on a network partner client and has stray rows in the seed data: they are never read or carried.
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsernameAsync("np@test.com");

        Assert.Empty(user!.BranchIds);
    }

    [Fact]
    public async Task FetchUserByUsername_WithNoBranchRows_HasNoScope()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsernameAsync("john@test.com");

        Assert.Empty(user!.BranchIds);
    }

    [Fact]
    public async Task FetchAgentContact_LoadsTheContactsBranchScope_SoAnAgentPassIsScopedToo()
    {
        var (repo, context) = CreateRepo();
        var staffId = await AddStaffContactAsync(context, 2, 1);

        var scoped = await repo.FetchAgentContactAsync(staffId);
        var unscoped = await repo.FetchAgentContactAsync(1);

        Assert.Equal([1, 2], scoped!.BranchIds);
        Assert.Empty(unscoped!.BranchIds);
        Assert.Null(await repo.FetchAgentContactAsync(999999));
    }

    [Fact]
    public async Task GetContactBranchIds_ReturnsTheCurrentScope_EmptyWhenNone_AndNullWhenTheContactIsInactiveOrGone()
    {
        var (repo, _) = CreateRepo();

        Assert.Equal([1, 2], await repo.GetContactBranchIdsAsync(5));
        Assert.Empty((await repo.GetContactBranchIdsAsync(1))!);
        Assert.Null(await repo.GetContactBranchIdsAsync(2));        // inactive: never turned into "no scope"
        Assert.Null(await repo.GetContactBranchIdsAsync(999999));   // gone
    }

    [Fact]
    public async Task ABranchReadFailureThatIsNotAMissingTable_IsThrown_NeverTreatedAsNoScope()
    {
        // A permission error (229) or any other failure must not quietly widen what someone sees. SQLite raises its own
        // exception type here, which is exactly "some other failure": it must surface.
        var (repo, context) = CreateRepo();
        var staffId = await AddStaffContactAsync(context);
        await context.Database.ExecuteSqlRawAsync("DROP TABLE tblContactBranch", TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => repo.FetchUserByUsernameAsync("tenant.staff@test.com"));
        await Assert.ThrowsAnyAsync<Exception>(() => repo.FetchAgentContactAsync(staffId));
        await Assert.ThrowsAnyAsync<Exception>(() => repo.GetContactBranchIdsAsync(1));
    }

    [Fact]
    public async Task NonStaffSignIn_IsNotCoupledToTheBranchTable()
    {
        // Customers, network partners and DF Admin never carry a restrictive claim, so an unreadable branch table (here:
        // gone, standing in for a 229 permission error) must not stop them signing in. Staff above still fail closed.
        var (repo, context) = CreateRepo();
        await context.Database.ExecuteSqlRawAsync("DROP TABLE tblContactBranch", TestContext.Current.CancellationToken);

        var customer = await repo.FetchUserByUsernameAsync("john@test.com");
        var partner = await repo.FetchUserByUsernameAsync("np@test.com");
        var agentCustomer = await repo.FetchAgentContactAsync(1);

        Assert.NotNull(customer);
        Assert.NotNull(partner);
        Assert.NotNull(agentCustomer);
        Assert.Empty(customer.BranchIds);
        Assert.Empty(partner.BranchIds);
    }

    [Fact]
    public async Task AMissingBranchTable_Error208_MeansNoScopeForStaff_LoginCarriesOn()
    {
        // A real SqlException number 208 ("Invalid object name") on the branch read: a tenant that has not had the branch
        // migration yet, where nobody is scoped. Sign-in carries on with an empty list.
        var interceptor = new ThrowOnBranchTableInterceptor(SqlExceptionFactory.Create(TenantSqlErrors.InvalidObjectName));
        var context = TestDespatchContextFactory.CreateWithSeedData(interceptor: interceptor);
        var repo = new Repository(context, Substitute.For<ITenantService>());
        var staffId = await AddStaffContactAsync(context, 1, 2);
        interceptor.Armed = true;

        var user = await repo.FetchUserByUsernameAsync("tenant.staff@test.com");
        var agent = await repo.FetchAgentContactAsync(staffId);
        var refresh = await repo.GetContactBranchIdsAsync(staffId);

        Assert.NotNull(user);
        Assert.Empty(user.BranchIds);
        Assert.Empty(agent!.BranchIds);
        Assert.Empty(refresh!);
    }

    [Theory]
    [InlineData(229)]    // permission denied
    [InlineData(18456)]  // login failed
    [InlineData(-2)]     // timeout
    public async Task AnyOtherSqlServerError_OnTheBranchRead_IsThrown_ForStaff(int number)
    {
        var sql = SqlExceptionFactory.Create(number);
        var interceptor = new ThrowOnBranchTableInterceptor(sql);
        var context = TestDespatchContextFactory.CreateWithSeedData(interceptor: interceptor);
        var repo = new Repository(context, Substitute.For<ITenantService>());
        var staffId = await AddStaffContactAsync(context, 1);
        interceptor.Armed = true;

        var ex1 = await Assert.ThrowsAnyAsync<Exception>(() => repo.FetchUserByUsernameAsync("tenant.staff@test.com"));
        var ex2 = await Assert.ThrowsAnyAsync<Exception>(() => repo.FetchAgentContactAsync(staffId));
        var ex3 = await Assert.ThrowsAnyAsync<Exception>(() => repo.GetContactBranchIdsAsync(staffId));

        Assert.All(new[] { ex1, ex2, ex3 }, e => Assert.Equal(number, Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e).Number));
    }

    [Fact]
    public void ARealSqlException208_IsTheOnlyOneTolerated()
    {
        Assert.True(TenantSqlErrors.IsMissingTable(SqlExceptionFactory.Create(208)));
        Assert.False(TenantSqlErrors.IsMissingTable(SqlExceptionFactory.Create(229)));
    }

    [Theory]
    [InlineData(208, true)]     // Invalid object name: the tenant has not had the branch migration yet
    [InlineData(229, false)]    // permission denied: must be thrown
    [InlineData(18456, false)]  // login failed
    [InlineData(0, false)]
    public void OnlyErrorNumber208IsTolerated(int number, bool tolerated) =>
        Assert.Equal(tolerated, TenantSqlErrors.IsMissingTableNumber(number));

    [Fact]
    public void ExceptionsThatAreNotSqlServerErrors_AreNeverTolerated()
    {
        Assert.False(TenantSqlErrors.IsMissingTable(new InvalidOperationException("boom")));
        Assert.False(TenantSqlErrors.IsMissingTable(new Microsoft.Data.Sqlite.SqliteException("no such table", 1)));
    }

    [Fact]
    public async Task FetchUserByUsername_ActiveUser_IncludesClient()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsernameAsync("john@test.com");

        Assert.NotNull(user!.UcctClient);
        Assert.Equal("Test Client", user.UcctClient!.UcclName);
    }

    [Fact]
    public async Task FetchUserByUsername_InactiveUser_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsernameAsync("jane@test.com");

        Assert.Null(user);
    }

    [Fact]
    public async Task FetchUserByUsername_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsernameAsync("nobody@test.com");

        Assert.Null(user);
    }

    // FetchSubAccountsAsync tests
    [Fact]
    public async Task FetchSubAccountsAsync_WithSubAccounts_ReturnsCommaSeparated()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.FetchSubAccountsAsync(1);

        Assert.Contains("2", result);
    }

    [Fact]
    public async Task FetchSubAccountsAsync_NoSubAccounts_ReturnsEmpty()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.FetchSubAccountsAsync(999);

        Assert.Empty(result);
    }

    // ValidateCourierByEmail tests
    [Fact]
    public async Task ValidateCourierByEmail_ActiveCourier_ReturnsId()
    {
        var (repo, _) = CreateRepo();

        var id = await repo.ValidateCourierByEmailAsync("courier@test.com");

        Assert.Equal(1, id);
    }

    [Fact]
    public async Task ValidateCourierByEmail_InactiveCourier_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var id = await repo.ValidateCourierByEmailAsync("inactive@test.com");

        Assert.Null(id);
    }

    [Fact]
    public async Task ValidateCourierByEmail_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var id = await repo.ValidateCourierByEmailAsync("nobody@test.com");

        Assert.Null(id);
    }

    [Fact]
    public async Task ValidateCourierByEmail_TrimmedEmail_ReturnsId()
    {
        var (repo, context) = CreateRepo();
        // Add a courier with whitespace in email
        context.TucCouriers.Add(new TucCourier
        {
            UccrId = 3,
            Code = "CR003",
            UccrName = "Trimmed",
            UccrSurname = "Courier",
            UccrEmail = " trimmed@test.com ",
            Active = true,
            Created = DateTime.Now,
            CreatedBy = "test",
            LastModified = DateTime.Now,
            LastModifiedBy = "test"
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var id = await repo.ValidateCourierByEmailAsync("trimmed@test.com");

        Assert.Equal(3, id);
    }

    // GetAccountsModeAsync tests
    [Fact]
    public async Task GetAccountsModeAsync_WithSettings_ReturnsValue()
    {
        var (repo, _) = CreateRepo();

        var mode = await repo.GetAccountsModeAsync();

        Assert.Equal(2, mode);
    }

    [Fact]
    public async Task GetAccountsModeAsync_NoSettings_ReturnsNull()
    {
        var context = TestDespatchContextFactory.Create();
        var tenantService = Substitute.For<ITenantService>();
        var repo = new Repository(context, tenantService);

        var mode = await repo.GetAccountsModeAsync();

        Assert.Null(mode);
    }

    // IsAfterHoursAuthorized tests
    [Fact]
    public async Task IsAfterHoursAuthorized_RecordExists_ReturnsTrue()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.IsAfterHoursAuthorizedAsync(1);

        Assert.True(result);
    }

    [Fact]
    public async Task IsAfterHoursAuthorized_NoRecord_ReturnsFalse()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.IsAfterHoursAuthorizedAsync(999);

        Assert.False(result);
    }

    // UpdateUserAccessedAsync tests
    [Fact]
    public async Task UpdateUserAccessedAsync_ExistingContact_UpdatesFields()
    {
        var (repo, context) = CreateRepo();

        await repo.UpdateUserAccessedAsync(1, true, 1);

        // ExecuteUpdateAsync bypasses the change tracker, so clear it before re-querying
        context.ChangeTracker.Clear();
        var contact = await context.TucClientContacts.FindAsync([1], TestContext.Current.CancellationToken);
        Assert.True(contact!.AllowCookieLogin);
        Assert.NotNull(contact.LastAccessed);
        Assert.InRange(contact.LastAccessed.Value, DateTime.UtcNow - TimeSpan.FromSeconds(5), DateTime.UtcNow + TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateUserAccessedAsync_NonExistentContact_NoOp()
    {
        var (repo, _) = CreateRepo();

        // Should not throw
        await repo.UpdateUserAccessedAsync(999, false, 1);
    }

    // GetDespatchWebInternetPermissions tests (stored proc mock)
    [Fact]
    public async Task GetDespatchWebInternetPermissions_WithMockedProcedures_ReturnsData()
    {
        var (repo, context) = CreateRepo();
        var mockProcs = Substitute.For<IDespatchContextProcedures>();
        mockProcs.RVW_stpValidateInternetPermissionsAsync(
                Arg.Any<int?>(), Arg.Any<OutputParameter<int>>(), Arg.Any<CancellationToken>())
            .Returns([
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 2, ClientContactID = 1 },
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 12, ClientContactID = 1 }
            ]);
        context.Procedures = mockProcs;

        var result = await repo.GetDespatchWebInternetPermissionsAsync(1);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.InternetPermissionID == 12);
    }
}
