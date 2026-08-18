using Hub.Models.Master;
using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IAuthenticationRepository
{
    Task<User?> GetUserByEmailAsync(string email, bool? isCourier = null);
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
