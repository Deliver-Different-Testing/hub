using Hub.Interfaces;
using Hub.Models;
using Hub.Repositories;
using Hub.Tests.Helpers;
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

        var user = await repo.FetchUserByUsername("john@test.com");

        Assert.NotNull(user);
        Assert.Equal("John", user.UcctFirstname);
    }

    [Fact]
    public async Task FetchUserByUsername_ActiveUser_IncludesClient()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsername("john@test.com");

        Assert.NotNull(user!.UcctClient);
        Assert.Equal("Test Client", user.UcctClient!.UcclName);
    }

    [Fact]
    public async Task FetchUserByUsername_InactiveUser_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsername("jane@test.com");

        Assert.Null(user);
    }

    [Fact]
    public async Task FetchUserByUsername_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsername("nobody@test.com");

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

        var id = await repo.ValidateCourierByEmail("courier@test.com");

        Assert.Equal(1, id);
    }

    [Fact]
    public async Task ValidateCourierByEmail_InactiveCourier_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var id = await repo.ValidateCourierByEmail("inactive@test.com");

        Assert.Null(id);
    }

    [Fact]
    public async Task ValidateCourierByEmail_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var id = await repo.ValidateCourierByEmail("nobody@test.com");

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

        var id = await repo.ValidateCourierByEmail("trimmed@test.com");

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

        var result = await repo.IsAfterHoursAuthorized(1);

        Assert.True(result);
    }

    [Fact]
    public async Task IsAfterHoursAuthorized_NoRecord_ReturnsFalse()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.IsAfterHoursAuthorized(999);

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

        var result = await repo.GetDespatchWebInternetPermissions(1);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.InternetPermissionID == 12);
    }
}
