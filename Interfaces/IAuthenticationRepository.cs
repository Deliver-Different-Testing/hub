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
}
