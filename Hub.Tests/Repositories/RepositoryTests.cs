using FluentAssertions;
using Hub.Models;
using Hub.Repositories;
using Hub.Tests.Helpers;
using Moq;

namespace Hub.Tests.Repositories;

public class RepositoryTests
{
    private static (Repository repo, DynamicDespatchDbContext context) CreateRepo()
    {
        var context = TestDespatchContextFactory.CreateWithSeedData();
        var repo = new Repository(context);
        return (repo, context);
    }

    // FetchUserByUsername tests
    [Fact]
    public async Task FetchUserByUsername_ActiveUser_ReturnsUser()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsername("john@test.com");

        user.Should().NotBeNull();
        user!.UcctFirstname.Should().Be("John");
    }

    [Fact]
    public async Task FetchUserByUsername_ActiveUser_IncludesClient()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsername("john@test.com");

        user!.UcctClient.Should().NotBeNull();
        user.UcctClient!.UcclName.Should().Be("Test Client");
    }

    [Fact]
    public async Task FetchUserByUsername_InactiveUser_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsername("jane@test.com");

        user.Should().BeNull();
    }

    [Fact]
    public async Task FetchUserByUsername_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.FetchUserByUsername("nobody@test.com");

        user.Should().BeNull();
    }

    // FetchSubAccountsAsync tests
    [Fact]
    public async Task FetchSubAccountsAsync_WithSubAccounts_ReturnsCommaSeparated()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.FetchSubAccountsAsync(1);

        result.Should().Contain("2");
    }

    [Fact]
    public async Task FetchSubAccountsAsync_NoSubAccounts_ReturnsEmpty()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.FetchSubAccountsAsync(999);

        result.Should().BeEmpty();
    }

    // ValidateCourierByEmail tests
    [Fact]
    public async Task ValidateCourierByEmail_ActiveCourier_ReturnsId()
    {
        var (repo, _) = CreateRepo();

        var id = await repo.ValidateCourierByEmail("courier@test.com");

        id.Should().Be(1);
    }

    [Fact]
    public async Task ValidateCourierByEmail_InactiveCourier_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var id = await repo.ValidateCourierByEmail("inactive@test.com");

        id.Should().BeNull();
    }

    [Fact]
    public async Task ValidateCourierByEmail_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var id = await repo.ValidateCourierByEmail("nobody@test.com");

        id.Should().BeNull();
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
        await context.SaveChangesAsync();

        var id = await repo.ValidateCourierByEmail("trimmed@test.com");

        id.Should().Be(3);
    }

    // GetAccountsModeAsync tests
    [Fact]
    public async Task GetAccountsModeAsync_WithSettings_ReturnsValue()
    {
        var (repo, _) = CreateRepo();

        var mode = await repo.GetAccountsModeAsync();

        mode.Should().Be(2);
    }

    [Fact]
    public async Task GetAccountsModeAsync_NoSettings_ReturnsNull()
    {
        var context = TestDespatchContextFactory.Create();
        var repo = new Repository(context);

        var mode = await repo.GetAccountsModeAsync();

        mode.Should().BeNull();
    }

    // IsAfterHoursAuthorized tests
    [Fact]
    public async Task IsAfterHoursAuthorized_RecordExists_ReturnsTrue()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.IsAfterHoursAuthorized(1, 1);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAfterHoursAuthorized_NoRecord_ReturnsFalse()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.IsAfterHoursAuthorized(999, 1);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAfterHoursAuthorized_IgnoresDayOfWeek()
    {
        var (repo, _) = CreateRepo();

        // The record has WeekDay=1 but logic ignores day, only checks existence
        var result = await repo.IsAfterHoursAuthorized(1, 5);

        result.Should().BeTrue();
    }

    // UpdateUserAccessed tests
    [Fact]
    public void UpdateUserAccessed_ExistingContact_UpdatesFields()
    {
        var (repo, context) = CreateRepo();

        repo.UpdateUserAccessed(1, true);

        var contact = context.TucClientContacts.Find(1);
        contact!.AllowCookieLogin.Should().BeTrue();
        contact.LastAccessed.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void UpdateUserAccessed_NonExistentContact_NoOp()
    {
        var (repo, _) = CreateRepo();

        // Should not throw
        var act = () => repo.UpdateUserAccessed(999, false);
        act.Should().NotThrow();
    }

    // GetDespatchWebInternetPermissions tests (stored proc mock)
    [Fact]
    public async Task GetDespatchWebInternetPermissions_WithMockedProcedures_ReturnsData()
    {
        var (repo, context) = CreateRepo();
        var mockProcs = new Mock<IDespatchContextProcedures>();
        mockProcs
            .Setup(p => p.RVW_stpValidateInternetPermissionsAsync(
                It.IsAny<int?>(), It.IsAny<OutputParameter<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 2, ClientContactID = 1 },
                new RVW_stpValidateInternetPermissionsResult { InternetPermissionID = 12, ClientContactID = 1 }
            ]);
        context.Procedures = mockProcs.Object;

        var result = await repo.GetDespatchWebInternetPermissions(1);

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.InternetPermissionID == 12);
    }
}
