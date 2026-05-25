using Hub.Models.Master;
using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IAuthenticationRepository
{
    Task<User?> GetUserByEmail(string email, bool? isCourier = null);
    Task<IReadOnlyList<TenantUserSettingViewModel>> GetUserSettings(int tenantId, int userId);
    Task SaveUserSetting(TenantUserSettingViewModel viewModel, int tenantId, int userId);
    Task SaveAsync();
    Task<User?> GetUserById(int id);
    Task<User?> GetUserByResetKey(string resetKey);
    Task<IReadOnlyList<Tenant>> GetTenantsByUserIdAsync(int userId);
    Task<bool> UpdateCurrentTenantIdAsync(int userId, int tenantId);
    Task<string?> GetTenantTimeZoneAsync(int tenantId);
    Task<string?> GetTenantConnectionStringAsync(int tenantId);

    // Phase 5+28a §B.1 — Network Partner user provisioning, called from
    // DfrntDriveConfigurator's NP creation cascade. Inserts a Master DB
    // User row with IsNetworkPartner=true, an empty password (the invite
    // flow's set-password step populates it), and a freshly-generated
    // ResetKey that gets embedded in the invite-email link. The caller
    // is responsible for triggering the email send via the existing
    // tenant DB stored proc (the same one `ForgotPassword` uses).
    //
    // Returns null when a User with that email already exists in Master DB
    // (the caller should surface that as a 409 — re-invite-existing is a
    // separate slice).
    Task<User?> CreateNpUserAsync(string email, int currentTenantId);
}
