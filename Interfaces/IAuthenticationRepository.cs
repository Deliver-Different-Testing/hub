using Hub.Models.Master;
using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IAuthenticationRepository
{
    Task<User?> GetUserByEmailAsync(string email, bool? isCourier = null);

    // Switching a courier on. Its existence is what makes a courier available to a merchant at all,
    // and this is still the only writer - nothing calls it yet, so the row is seeded by hand.
    Task<bool> UpsertShopifyTenantHostAsync(int tenantId, string integrationManagerUrl);

    Task<IReadOnlyList<TenantUserSettingViewModel>> GetUserSettingsAsync(int tenantId, int userId);
    Task SaveUserSettingAsync(TenantUserSettingViewModel viewModel, int tenantId, int userId);
    Task SaveAsync();
    Task<User?> GetUserByIdAsync(int id);
    Task<User?> GetUserByResetKeyAsync(string resetKey);
    Task<IReadOnlyList<Tenant>> GetTenantsByUserIdAsync(int userId);
    Task<bool> UpdateCurrentTenantIdAsync(int userId, int tenantId);
    Task<bool> IsUserAssociatedWithTenantAsync(int userId, int tenantId);
    Task<string?> GetTenantTimeZoneAsync(int tenantId);
    Task<string?> GetTenantConnectionStringAsync(int tenantId);

    // Generalised provisioning shared by the NP cascade and the configurator's
    // tenant-user (Team page) cascade. Creates a Master DB User row with an empty
    // password and a fresh ResetKey for the invite-email link; isNetworkPartner
    // toggles only the data-scope flag. Returns null when the email already exists.
    Task<User?> CreateUserAsync(string email, int currentTenantId, bool isNetworkPartner);

    // 2026-08-18 — collision guard for the change-email endpoint. Deliberately
    // NOT filtered by IsCourier: Master.User.Email is the login key for every
    // scheme, so handing a staff user an address a courier already holds would
    // make the login ambiguous. Any existing holder blocks the rename.
    Task<bool> EmailExistsAsync(string email);
}
