using FluentAssertions;
using Hub.Models.Master;
using Hub.Repositories;
using Hub.Tests.Helpers;
using Hub.ViewModels;

namespace Hub.Tests.Repositories;

public class AuthenticationRepositoryTests
{
    private static (AuthenticationRepository repo, MasterContext context) CreateRepo()
    {
        var context = TestMasterContextFactory.CreateWithSeedData();
        var repo = new AuthenticationRepository(context);
        return (repo, context);
    }

    // GetUserByEmail tests
    [Fact]
    public async Task GetUserByEmail_StaffUser_WithIsCourierFalse_ReturnsStaff()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserByEmail("staff@test.com", false);

        user.Should().NotBeNull();
        user.Email.Should().Be("staff@test.com");
        user.IsCourier.Should().Be(false);
    }

    [Fact]
    public async Task GetUserByEmail_CourierUser_WithIsCourierTrue_ReturnsCourier()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserByEmail("courier@test.com", true);

        user.Should().NotBeNull();
        user.Email.Should().Be("courier@test.com");
        user.IsCourier.Should().Be(true);
    }

    [Fact]
    public async Task GetUserByEmail_WithoutFilter_ReturnsUser()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserByEmail("staff@test.com");

        user.Should().NotBeNull();
        user.Email.Should().Be("staff@test.com");
    }

    [Fact]
    public async Task GetUserByEmail_IncludesCurrentTenant()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserByEmail("staff@test.com");

        user.Should().NotBeNull();
        user.CurrentTenant.Should().NotBeNull();
        user.CurrentTenant!.Code.Should().Be("test");
    }

    [Fact]
    public async Task GetUserByEmail_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserByEmail("nonexistent@test.com");

        user.Should().BeNull();
    }

    [Fact]
    public async Task GetUserByEmail_StaffFilterForCourier_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserByEmail("courier@test.com", false);

        user.Should().BeNull();
    }

    // GetUserById tests
    [Fact]
    public async Task GetUserById_Found_ReturnsUser()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserById(1);

        user.Should().NotBeNull();
        user.Email.Should().Be("staff@test.com");
    }

    [Fact]
    public async Task GetUserById_IncludesCurrentTenant()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserById(1);

        user!.CurrentTenant.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserById_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserById(999);

        user.Should().BeNull();
    }

    // GetUserByResetKey tests
    [Fact]
    public async Task GetUserByResetKey_Found_ReturnsUser()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserByResetKey("valid-reset-key");

        user.Should().NotBeNull();
        user.Email.Should().Be("reset@test.com");
    }

    [Fact]
    public async Task GetUserByResetKey_NotFound_ReturnsNull()
    {
        var (repo, _) = CreateRepo();

        var user = await repo.GetUserByResetKey("invalid-key");

        user.Should().BeNull();
    }

    // GetTenantsByUserIdAsync tests
    [Fact]
    public async Task GetTenantsByUserIdAsync_WithTenants_ReturnsTenants()
    {
        var (repo, _) = CreateRepo();

        var tenants = await repo.GetTenantsByUserIdAsync(1);

        tenants.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTenantsByUserIdAsync_NoTenants_ReturnsEmpty()
    {
        var (repo, _) = CreateRepo();

        var tenants = await repo.GetTenantsByUserIdAsync(999);

        tenants.Should().BeEmpty();
    }

    // UpdateCurrentTenantIdAsync tests
    [Fact]
    public async Task UpdateCurrentTenantIdAsync_ValidUpdate_ReturnsTrue()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.UpdateCurrentTenantIdAsync(1, 2);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateCurrentTenantIdAsync_PersistsChange()
    {
        var (repo, _) = CreateRepo();

        await repo.UpdateCurrentTenantIdAsync(1, 2);

        var user = await repo.GetUserById(1);
        user!.CurrentTenantId.Should().Be(2);
    }

    [Fact]
    public async Task UpdateCurrentTenantIdAsync_NotAssociated_ReturnsFalse()
    {
        var (repo, _) = CreateRepo();

        // User 2 (courier) is only associated with tenant 1, not tenant 2
        var result = await repo.UpdateCurrentTenantIdAsync(2, 2);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateCurrentTenantIdAsync_NonExistentUser_ReturnsFalse()
    {
        var (repo, _) = CreateRepo();

        var result = await repo.UpdateCurrentTenantIdAsync(999, 1);

        result.Should().BeFalse();
    }

    // GetUserSettings tests
    [Fact]
    public async Task GetUserSettings_WithSettings_ReturnsSettings()
    {
        var (repo, _) = CreateRepo();

        var settings = await repo.GetUserSettings(1, 1);

        var tenantUserSettingViewModels = settings as TenantUserSettingViewModel[] ?? settings.ToArray();
        tenantUserSettingViewModels.Should().ContainSingle();
        tenantUserSettingViewModels.First().Name.Should().Be("Theme");
        tenantUserSettingViewModels.First().Value.Should().Be("Dark");
    }

    [Fact]
    public async Task GetUserSettings_NoSettings_ReturnsEmpty()
    {
        var (repo, _) = CreateRepo();

        var settings = await repo.GetUserSettings(2, 2);

        settings.Should().BeEmpty();
    }

    // SaveUserSetting tests
    [Fact]
    public async Task SaveUserSetting_NewSetting_CreatesIt()
    {
        var (repo, _) = CreateRepo();
        var viewModel = new TenantUserSettingViewModel { Name = "Language", Value = "en" };

        await repo.SaveUserSetting(viewModel, 1, 1);

        var settings = await repo.GetUserSettings(1, 1);
        settings.Should().Contain(s => s.Name == "Language" && s.Value == "en");
    }

    [Fact]
    public async Task SaveUserSetting_ExistingSetting_UpdatesIt()
    {
        var (repo, _) = CreateRepo();
        var viewModel = new TenantUserSettingViewModel { Name = "Theme", Value = "Light" };

        await repo.SaveUserSetting(viewModel, 1, 1);

        var settings = await repo.GetUserSettings(1, 1);
        var tenantUserSettingViewModels = settings.ToList();
        tenantUserSettingViewModels.Should().ContainSingle(s => s.Name == "Theme");
        tenantUserSettingViewModels.First(s => s.Name == "Theme").Value.Should().Be("Light");
    }
}
