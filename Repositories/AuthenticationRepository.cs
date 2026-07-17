using Hub.Interfaces;
using Hub.Models.Master;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Hub.ViewModels;

namespace Hub.Repositories;

public sealed class AuthenticationRepository(MasterContext context) : IAuthenticationRepository
{
    public async Task<User?> GetUserByEmailAsync(string email, bool? isCourier = null)
    {
        var query = context.Users
            .Include(u => u.CurrentTenant)
            .Where(u => u.Email == email);

        // Filter by IsCourier if specified
        if (isCourier.HasValue)
        {
            // Looking for courier: IsCourier must be true
            query = isCourier.Value
                ? query.Where(u => u.IsCourier == true)
                :
                // Looking for staff: IsCourier must be false or null
                query.Where(u => u.IsCourier == false || u.IsCourier == null);
        }

        return await query.FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<TenantUserSettingViewModel>> GetUserSettingsAsync(int tenantId, int userId) =>
        await context.TenantUserSettings
            .AsNoTracking()
            .Where(tus => tus.TenantId == tenantId && tus.UserId == userId)
            .Select(tus => new TenantUserSettingViewModel
            {
                Id = tus.TenantUserSettingId,
                Name = tus.SettingName,
                Value = tus.SettingValue
            })
            .ToListAsync();

    public async Task SaveUserSettingAsync(TenantUserSettingViewModel viewModel, int tenantId, int userId)
    {
        // Check if setting already exists
        var existingSetting = await context.TenantUserSettings
            .FirstOrDefaultAsync(tus =>
                tus.TenantId == tenantId &&
                tus.UserId == userId &&
                tus.SettingName == viewModel.Name);

        if (existingSetting != null)
        {
            // Update existing setting
            existingSetting.SettingValue = viewModel.Value;
        }
        else
        {
            // Create new setting
            var newSetting = new TenantUserSetting
            {
                TenantId = tenantId,
                UserId = userId,
                SettingName = viewModel.Name,
                SettingValue = viewModel.Value
            };

            await context.TenantUserSettings.AddAsync(newSetting);
        }

        await context.SaveChangesAsync();
    }

    public async Task SaveAsync() => await context.SaveChangesAsync();

    public async Task<User?> GetUserByIdAsync(int id) =>
        await context.Users
            .AsNoTracking()
            .Include(u => u.CurrentTenant)
            .FirstOrDefaultAsync(u => u.UserId == id);

    public async Task<User?> GetUserByResetKeyAsync(string resetKey) =>
        await context.Users
            .Include(u => u.CurrentTenant)
            .FirstOrDefaultAsync(u => u.ResetKey == resetKey);

    public async Task<IReadOnlyList<Tenant>> GetTenantsByUserIdAsync(int userId) =>
        await context.TenantUsers.AsNoTracking().Where(tu => tu.UserId == userId).Select(tu => tu.Tenant)
            .Distinct()
            .ToListAsync();

    public async Task<string?> GetTenantTimeZoneAsync(int tenantId) =>
        await context.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TimeZone)
            .FirstOrDefaultAsync();

    public async Task<string?> GetTenantConnectionStringAsync(int tenantId) =>
        await context.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.Dbconnection)
            .FirstOrDefaultAsync();

    public async Task<bool> IsUserAssociatedWithTenantAsync(int userId, int tenantId) =>
        await context.TenantUsers
            .AnyAsync(tu => tu.UserId == userId && tu.TenantId == tenantId);

    public async Task<bool> UpdateCurrentTenantIdAsync(int userId, int tenantId)
    {
        var user = await context.Users.FindAsync(userId);

        if (user == null)
        {
            return false;
        }

        // Check if the user is associated with the tenant
        if (!await IsUserAssociatedWithTenantAsync(userId, tenantId))
        {
            return false;
        }

        user.CurrentTenantId = tenantId;

        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex)
        {
            Log.Error(ex, "Error updating Current Tenant. User {UserId} Tenant {TenantId}", userId, tenantId);
            return false;
        }
    }

    public async Task<User?> CreateNpUserAsync(string email, int currentTenantId) =>
        await CreateUserAsync(email, currentTenantId, isNetworkPartner: true);

    public async Task<User?> CreateUserAsync(string email, int currentTenantId, bool isNetworkPartner)
    {
        // 409-shape: don't insert if email already exists in Master.User.
        // Caller surfaces this to the operator as "user already exists in
        // Hub — manual remediation needed". Re-invite-existing is a
        // separate slice (idempotency Open Question #3 in the brief).
        var existing = await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email);
     
        if (existing)
        {
            return null;
        }

        var user = new User
        {
            Email = email,
            // Password / Salt are empty until the invitee sets their
            // password via the reset-key flow. IsLegacyHash=false marks
            // this row as PBKDF2/SHA256-ready when the password gets set.
            Password = string.Empty,
            Salt = string.Empty,
            IsLegacyHash = false,
            ResetKey = Guid.NewGuid().ToString(),
            CurrentTenantId = currentTenantId,
            // Tenant users (configurator Team page) and Network Partners share
            // this provisioning path; only the IsNetworkPartner data-scope flag
            // differs. Neither is a courier.
            IsNetworkPartner = isNetworkPartner,
            IsCourier = false
        };

        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
        return user;
    }
}