using Hub.Repositories;
using Hub.Tests.Helpers;
using Hub.ViewModels;

namespace Hub.Tests.Repositories;

public class AuthenticationRepositoryTests
{
    private static AuthenticationRepository CreateRepo()
    {
        var context = TestMasterContextFactory.CreateWithSeedData();
        return new AuthenticationRepository(context);
    }

    // GetUserByEmail tests
    [Fact]
    public async Task GetUserByEmail_StaffUser_WithIsCourierFalse_ReturnsStaff()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmail("staff@test.com", false);

        Assert.NotNull(user);
        Assert.Equal("staff@test.com", user.Email);
        Assert.Equal(false, user.IsCourier);
    }

    [Fact]
    public async Task GetUserByEmail_CourierUser_WithIsCourierTrue_ReturnsCourier()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmail("courier@test.com", true);

        Assert.NotNull(user);
        Assert.Equal("courier@test.com", user.Email);
        Assert.Equal(true, user.IsCourier);
    }

    [Fact]
    public async Task GetUserByEmail_WithoutFilter_ReturnsUser()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmail("staff@test.com");

        Assert.NotNull(user);
        Assert.Equal("staff@test.com", user.Email);
    }

    [Fact]
    public async Task GetUserByEmail_IncludesCurrentTenant()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmail("staff@test.com");

        Assert.NotNull(user);
        Assert.NotNull(user.CurrentTenant);
        Assert.Equal("test", user.CurrentTenant!.Code);
    }

    [Fact]
    public async Task GetUserByEmail_NotFound_ReturnsNull()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmail("nonexistent@test.com");

        Assert.Null(user);
    }

    [Fact]
    public async Task GetUserByEmail_StaffFilterForCourier_ReturnsNull()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmail("courier@test.com", false);

        Assert.Null(user);
    }

    // GetUserById tests
    [Fact]
    public async Task GetUserById_Found_ReturnsUser()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserById(1);

        Assert.NotNull(user);
        Assert.Equal("staff@test.com", user.Email);
    }

    [Fact]
    public async Task GetUserById_IncludesCurrentTenant()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserById(1);

        Assert.NotNull(user!.CurrentTenant);
    }

    [Fact]
    public async Task GetUserById_NotFound_ReturnsNull()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserById(999);

        Assert.Null(user);
    }

    // GetUserByResetKey tests
    [Fact]
    public async Task GetUserByResetKey_Found_ReturnsUser()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByResetKey("valid-reset-key");

        Assert.NotNull(user);
        Assert.Equal("reset@test.com", user.Email);
    }

    [Fact]
    public async Task GetUserByResetKey_NotFound_ReturnsNull()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByResetKey("invalid-key");

        Assert.Null(user);
    }

    // GetTenantsByUserIdAsync tests
    [Fact]
    public async Task GetTenantsByUserIdAsync_WithTenants_ReturnsTenants()
    {
        var repo = CreateRepo();

        var tenants = await repo.GetTenantsByUserIdAsync(1);

        Assert.Equal(2, tenants.Count);
    }

    [Fact]
    public async Task GetTenantsByUserIdAsync_NoTenants_ReturnsEmpty()
    {
        var repo = CreateRepo();

        var tenants = await repo.GetTenantsByUserIdAsync(999);

        Assert.Empty(tenants);
    }

    // UpdateCurrentTenantIdAsync tests
    [Fact]
    public async Task UpdateCurrentTenantIdAsync_ValidUpdate_ReturnsTrue()
    {
        var repo = CreateRepo();

        var result = await repo.UpdateCurrentTenantIdAsync(1, 2);

        Assert.True(result);
    }

    [Fact]
    public async Task UpdateCurrentTenantIdAsync_PersistsChange()
    {
        var repo = CreateRepo();

        await repo.UpdateCurrentTenantIdAsync(1, 2);

        var user = await repo.GetUserById(1);
        Assert.Equal(2, user!.CurrentTenantId);
    }

    [Fact]
    public async Task UpdateCurrentTenantIdAsync_GetUserById_ReturnsNewTenant()
    {
        // Regression: ba04c0e added NoTracking default to MasterContext which caused
        // UpdateCurrentTenantIdAsync to silently not persist, so GetUserById returned
        // the old tenant — breaking tenant switching and passing wrong tenant to apps.
        var repo = CreateRepo();

        await repo.UpdateCurrentTenantIdAsync(1, 2);

        var user = await repo.GetUserById(1);
        Assert.NotNull(user!.CurrentTenant);
        Assert.Equal(2, user.CurrentTenant!.TenantId);
        Assert.Equal("second", user.CurrentTenant.Code);
    }

    [Fact]
    public async Task UpdateCurrentTenantIdAsync_NotAssociated_ReturnsFalse()
    {
        var repo = CreateRepo();

        // User 2 (courier) is only associated with tenant 1, not tenant 2
        var result = await repo.UpdateCurrentTenantIdAsync(2, 2);

        Assert.False(result);
    }

    [Fact]
    public async Task UpdateCurrentTenantIdAsync_NonExistentUser_ReturnsFalse()
    {
        var repo = CreateRepo();

        var result = await repo.UpdateCurrentTenantIdAsync(999, 1);

        Assert.False(result);
    }

    // GetUserSettings tests
    [Fact]
    public async Task GetUserSettings_WithSettings_ReturnsSettings()
    {
        var repo = CreateRepo();

        var settings = await repo.GetUserSettings(1, 1);

        var tenantUserSettingViewModels = settings as TenantUserSettingViewModel[] ?? settings.ToArray();
        Assert.Single(tenantUserSettingViewModels);
        Assert.Equal("Theme", tenantUserSettingViewModels.First().Name);
        Assert.Equal("Dark", tenantUserSettingViewModels.First().Value);
    }

    [Fact]
    public async Task GetUserSettings_NoSettings_ReturnsEmpty()
    {
        var repo = CreateRepo();

        var settings = await repo.GetUserSettings(2, 2);

        Assert.Empty(settings);
    }

    // SaveUserSetting tests
    [Fact]
    public async Task SaveUserSetting_NewSetting_CreatesIt()
    {
        var repo = CreateRepo();
        var viewModel = new TenantUserSettingViewModel { Name = "Language", Value = "en" };

        await repo.SaveUserSetting(viewModel, 1, 1);

        var settings = await repo.GetUserSettings(1, 1);
        Assert.Contains(settings, s => s is { Name: "Language", Value: "en" });
    }

    [Fact]
    public async Task SaveUserSetting_ExistingSetting_UpdatesIt()
    {
        var repo = CreateRepo();
        var viewModel = new TenantUserSettingViewModel { Name = "Theme", Value = "Light" };

        await repo.SaveUserSetting(viewModel, 1, 1);

        var settings = await repo.GetUserSettings(1, 1);
        Assert.Single(settings, s => s.Name == "Theme");
        Assert.Equal("Light", settings.First(s => s.Name == "Theme").Value);
    }

    [Fact]
    public async Task SaveUserSetting_ExistingSetting_PersistsAcrossReads()
    {
        // Regression: NoTracking default on MasterContext caused SaveUserSetting to
        // load existing settings as untracked entities, so modifications were never saved.
        var repo = CreateRepo();

        await repo.SaveUserSetting(new TenantUserSettingViewModel { Name = "Theme", Value = "Blue" }, 1, 1);
        // Read again to confirm persistence (not just in-memory)
        var settings = await repo.GetUserSettings(1, 1);

        Assert.Equal("Blue", settings.First(s => s.Name == "Theme").Value);
    }
}
