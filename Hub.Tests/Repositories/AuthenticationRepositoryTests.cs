using Hub.Models.Master;
using Hub.Repositories;
using Hub.Tests.Helpers;
using Hub.ViewModels;
using Microsoft.EntityFrameworkCore;

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

        var user = await repo.GetUserByEmailAsync("staff@test.com", false);

        Assert.NotNull(user);
        Assert.Equal("staff@test.com", user.Email);
        Assert.Equal(false, user.IsCourier);
    }

    [Fact]
    public async Task GetUserByEmail_CourierUser_WithIsCourierTrue_ReturnsCourier()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmailAsync("courier@test.com", true);

        Assert.NotNull(user);
        Assert.Equal("courier@test.com", user.Email);
        Assert.Equal(true, user.IsCourier);
    }

    [Fact]
    public async Task GetUserByEmail_WithoutFilter_ReturnsUser()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmailAsync("staff@test.com");

        Assert.NotNull(user);
        Assert.Equal("staff@test.com", user.Email);
    }

    [Fact]
    public async Task GetUserByEmail_IncludesCurrentTenant()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmailAsync("staff@test.com");

        Assert.NotNull(user);
        Assert.NotNull(user.CurrentTenant);
        Assert.Equal("test", user.CurrentTenant!.Code);
    }

    [Fact]
    public async Task GetUserByEmail_NotFound_ReturnsNull()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmailAsync("nonexistent@test.com");

        Assert.Null(user);
    }

    [Fact]
    public async Task GetUserByEmail_StaffFilterForCourier_ReturnsNull()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByEmailAsync("courier@test.com", false);

        Assert.Null(user);
    }

    // GetUserById tests
    [Fact]
    public async Task GetUserById_Found_ReturnsUser()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByIdAsync(1);

        Assert.NotNull(user);
        Assert.Equal("staff@test.com", user.Email);
    }

    [Fact]
    public async Task GetUserById_IncludesCurrentTenant()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByIdAsync(1);

        Assert.NotNull(user!.CurrentTenant);
    }

    [Fact]
    public async Task GetUserById_NotFound_ReturnsNull()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByIdAsync(999);

        Assert.Null(user);
    }

    // GetUserByResetKey tests
    [Fact]
    public async Task GetUserByResetKey_Found_ReturnsUser()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByResetKeyAsync("valid-reset-key");

        Assert.NotNull(user);
        Assert.Equal("reset@test.com", user.Email);
    }

    [Fact]
    public async Task GetUserByResetKey_NotFound_ReturnsNull()
    {
        var repo = CreateRepo();

        var user = await repo.GetUserByResetKeyAsync("invalid-key");

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

        var user = await repo.GetUserByIdAsync(1);
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

        var user = await repo.GetUserByIdAsync(1);
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

    // IsUserAssociatedWithTenantAsync tests
    [Fact]
    public async Task IsUserAssociatedWithTenantAsync_Associated_ReturnsTrue()
    {
        var repo = CreateRepo();

        // User 1 (staff) is associated with both tenant 1 and tenant 2
        Assert.True(await repo.IsUserAssociatedWithTenantAsync(1, 2));
    }

    [Fact]
    public async Task IsUserAssociatedWithTenantAsync_NotAssociated_ReturnsFalse()
    {
        var repo = CreateRepo();

        // User 2 (courier) is only associated with tenant 1, not tenant 2
        Assert.False(await repo.IsUserAssociatedWithTenantAsync(2, 2));
    }

    // CreateUserAsync tests
    [Fact]
    public async Task CreateUserAsync_TenantUser_SetsIsNetworkPartnerFalseWithResetKey()
    {
        var repo = CreateRepo();

        var user = await repo.CreateUserAsync("newtenant@test.com", 1, isNetworkPartner: false);

        Assert.NotNull(user);
        Assert.False(user.IsNetworkPartner!.Value);
        Assert.False(user.IsCourier ?? false);
        Assert.False(string.IsNullOrEmpty(user.ResetKey));
        Assert.Equal(1, user.CurrentTenantId);
    }

    [Fact]
    public async Task CreateUserAsync_ExistingEmail_ReturnsNull()
    {
        var repo = CreateRepo();

        // staff@test.com already exists in Master.User (seed UserId 1).
        var user = await repo.CreateUserAsync("staff@test.com", 1, isNetworkPartner: false);

        Assert.Null(user);
    }

    [Fact]
    public async Task CreateNpUserAsync_StillSetsIsNetworkPartnerTrue()
    {
        var repo = CreateRepo();

        var user = await repo.CreateNpUserAsync("newnp@test.com", 1);

        Assert.NotNull(user);
        Assert.True(user.IsNetworkPartner!.Value);
    }

    // GetUserSettings tests
    [Fact]
    public async Task GetUserSettings_WithSettings_ReturnsSettings()
    {
        var repo = CreateRepo();

        var settings = await repo.GetUserSettingsAsync(1, 1);

        var tenantUserSettingViewModels = settings as TenantUserSettingViewModel[] ?? settings.ToArray();
        Assert.Single(tenantUserSettingViewModels);
        Assert.Equal("Theme", tenantUserSettingViewModels.First().Name);
        Assert.Equal("Dark", tenantUserSettingViewModels.First().Value);
    }

    [Fact]
    public async Task GetUserSettings_NoSettings_ReturnsEmpty()
    {
        var repo = CreateRepo();

        var settings = await repo.GetUserSettingsAsync(2, 2);

        Assert.Empty(settings);
    }

    // SaveUserSetting tests
    [Fact]
    public async Task SaveUserSetting_NewSetting_CreatesIt()
    {
        var repo = CreateRepo();
        var viewModel = new TenantUserSettingViewModel { Name = "Language", Value = "en" };

        await repo.SaveUserSettingAsync(viewModel, 1, 1);

        var settings = await repo.GetUserSettingsAsync(1, 1);
        Assert.Contains(settings, s => s is { Name: "Language", Value: "en" });
    }

    [Fact]
    public async Task SaveUserSetting_ExistingSetting_UpdatesIt()
    {
        var repo = CreateRepo();
        var viewModel = new TenantUserSettingViewModel { Name = "Theme", Value = "Light" };

        await repo.SaveUserSettingAsync(viewModel, 1, 1);

        var settings = await repo.GetUserSettingsAsync(1, 1);
        Assert.Single(settings, s => s.Name == "Theme");
        Assert.Equal("Light", settings.First(s => s.Name == "Theme").Value);
    }

    [Fact]
    public async Task SaveUserSetting_ExistingSetting_PersistsAcrossReads()
    {
        // Regression: NoTracking default on MasterContext caused SaveUserSetting to
        // load existing settings as untracked entities, so modifications were never saved.
        var repo = CreateRepo();

        await repo.SaveUserSettingAsync(new TenantUserSettingViewModel { Name = "Theme", Value = "Blue" }, 1, 1);
        // Read again to confirm persistence (not just in-memory)
        var settings = await repo.GetUserSettingsAsync(1, 1);

        Assert.Equal("Blue", settings.First(s => s.Name == "Theme").Value);
    }

    // ------------------------------------------------------------------ Shopify shop -> tenant

    [Fact]
    public async Task MapShopifyShop_NewPairing_IsRecordedAndReadableBack()
    {
        var repo = CreateRepo();

        Assert.Equal(ShopifyShopMappingResult.Mapped,
            await repo.MapShopifyShopAsync("a-shop.myshopify.com", 1));

        var mapping = await repo.GetTenantByShopifyShopAsync("a-shop.myshopify.com");

        Assert.NotNull(mapping);
        Assert.Equal(1, mapping.TenantId);
        Assert.Equal("test", mapping.TenantCode);
    }

    /// <summary>
    /// Shop is uniquely constrained rather than collated, so its casing has to be settled in code -
    /// otherwise the same merchant maps twice.
    /// </summary>
    [Theory]
    [InlineData("A-Shop.MyShopify.com")]
    [InlineData("  a-shop.myshopify.com  ")]
    public async Task GetTenantByShopifyShop_IsCaseAndWhitespaceInsensitive(string lookup)
    {
        var repo = CreateRepo();
        await repo.MapShopifyShopAsync("a-shop.myshopify.com", 1);

        Assert.NotNull(await repo.GetTenantByShopifyShopAsync(lookup));
    }

    /// <summary>
    /// The key is assigned by the repository, not the store: MasterContext configures
    /// ValueGeneratedNever so that SQL Server and the InMemory provider these tests run on write the
    /// same value. An insert that forgot it would write Guid.Empty, and the second one would collide
    /// on a key nobody chose.
    /// </summary>
    [Fact]
    public async Task MapShopifyShop_AssignsTheMappingItsOwnKey()
    {
        var context = TestMasterContextFactory.CreateWithSeedData();

        await new AuthenticationRepository(context).MapShopifyShopAsync("a-shop.myshopify.com", 1);

        var mapping = await context.ShopifyShopTenants.SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(Guid.Empty, mapping.Id);
    }

    /// <summary>
    /// The store's display name comes back with the mapping. Hub never writes it - the Shopify front
    /// door does, from the Admin API - so this only has to carry what is there, including nothing.
    /// </summary>
    [Fact]
    public async Task GetTenantByShopifyShop_CarriesTheStoreName()
    {
        var context = TestMasterContextFactory.CreateWithSeedData();
        context.ShopifyShopTenants.Add(new ShopifyShopTenant
        {
            Id = Guid.NewGuid(),
            Shop = "a-shop.myshopify.com",
            ShopName = "Acme Widgets",
            TenantId = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var mapping = await new AuthenticationRepository(context)
            .GetTenantByShopifyShopAsync("a-shop.myshopify.com");

        Assert.Equal("Acme Widgets", mapping?.ShopName);
    }

    [Fact]
    public async Task MapShopifyShop_SameShopSameTenant_IsIdempotent()
    {
        var repo = CreateRepo();
        await repo.MapShopifyShopAsync("a-shop.myshopify.com", 1);

        Assert.Equal(ShopifyShopMappingResult.AlreadyMapped,
            await repo.MapShopifyShopAsync("A-SHOP.myshopify.com", 1));
    }

    /// <summary>
    /// The failure this table exists to prevent: re-pointing a shop would move a live merchant's
    /// orders into a different courier's Despatch database.
    /// </summary>
    [Fact]
    public async Task MapShopifyShop_SameShopDifferentTenant_ConflictsAndLeavesTheOriginalIntact()
    {
        var repo = CreateRepo();
        await repo.MapShopifyShopAsync("a-shop.myshopify.com", 1);

        Assert.Equal(ShopifyShopMappingResult.ConflictsWithAnotherTenant,
            await repo.MapShopifyShopAsync("a-shop.myshopify.com", 2));

        var mapping = await repo.GetTenantByShopifyShopAsync("a-shop.myshopify.com");
        Assert.Equal(1, mapping!.TenantId);
    }

    [Fact]
    public async Task MapShopifyShop_UnknownTenant_IsRefused()
    {
        var repo = CreateRepo();

        Assert.Equal(ShopifyShopMappingResult.TenantNotFound,
            await repo.MapShopifyShopAsync("a-shop.myshopify.com", 999));

        Assert.Null(await repo.GetTenantByShopifyShopAsync("a-shop.myshopify.com"));
    }

    [Fact]
    public async Task MapShopifyShop_DifferentShopsCanShareATenant()
    {
        var repo = CreateRepo();

        Assert.Equal(ShopifyShopMappingResult.Mapped, await repo.MapShopifyShopAsync("one.myshopify.com", 1));
        Assert.Equal(ShopifyShopMappingResult.Mapped, await repo.MapShopifyShopAsync("two.myshopify.com", 1));
    }

    [Fact]
    public async Task GetTenantByShopifyShop_UnmappedShop_ReturnsNull()
    {
        var repo = CreateRepo();

        Assert.Null(await repo.GetTenantByShopifyShopAsync("nobody.myshopify.com"));
    }
}
