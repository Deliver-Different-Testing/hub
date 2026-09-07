using Hub.Models.Master;
using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IAuthenticationRepository
{
    Task<User?> GetUserByEmailAsync(string email, bool? isCourier = null);

    // Shopify merchant sign-in. Its own user lookup rather than the one above, because the app has a
    // single login box and must decide for itself that the account is a merchant's; and its own
    // tenant query, because membership - not User.CurrentTenantId - is what says which couriers a
    // merchant may connect a store to.
    Task<User?> GetShopifyMerchantUserAsync(string email);
    Task<IReadOnlyList<ShopifySignInTenant>> GetShopifyTenantsForUserAsync(int userId);

    // Switching a courier on. The row's existence is what the query above joins to, so this is the
    // writer that makes any of it work - and until now there was none.
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

    // The Deliver DFRNT Shopify app is one listing with one App URL, so its install callback,
    // /api/Rates carrier callback and webhooks all land on a single shared Integration Manager
    // deployment serving every tenant. The shop domain is the only tenant identity those requests
    // carry. IM holds no master-controller connection of its own, so the lookup lives here.
    Task<ShopifyShopTenantResponse?> GetTenantByShopifyShopAsync(string shop);

    // Written when a merchant signs in and their courier is settled. Never re-points an existing
    // mapping: a shop already recorded against another tenant comes back as a conflict, because
    // silently moving it would move a live merchant's orders into a different courier's database.
    Task<ShopifyShopMappingResult> MapShopifyShopAsync(string shop, int tenantId);

    // Removes a shop's mapping, but only if it belongs to the tenant named. Built for Shopify
    // requires a merchant be able to disconnect from inside the embedded app; scoping the delete to
    // the owning tenant is what stops one courier detaching another courier's store.
    Task<bool> UnmapShopifyShopAsync(string shop, int tenantId);

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
