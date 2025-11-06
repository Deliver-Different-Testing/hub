#nullable enable
using Hub.Models.Master;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hub.ViewModels;

namespace Hub.Repositories
{

    public class AuthenticationRepository(MasterContext context)
    {
        
        public async Task<User?> GetUserByEmail(string email, bool? isCourier = null)
        {
            var query = context.Users
                .Include(u => u.CurrentTenant)
                .Where(u => u.Email.ToLower() == email.ToLower());

            // Filter by IsCourier if specified
            if (isCourier.HasValue)
            {
                if (isCourier.Value)
                {
                    // Looking for courier: IsCourier must be true
                    query = query.Where(u => u.IsCourier == true);
                }
                else
                {
                    // Looking for staff: IsCourier must be false or null
                    query = query.Where(u => u.IsCourier == false || u.IsCourier == null);
                }
            }

            return await query.FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<TenantUserSettingViewModel>> GetUserSettings(int tenantId, int userId)
        {
            return await context.TenantUserSettings
                .Where(tus => tus.TenantId == tenantId && tus.UserId == userId)
                .Select(tus => new TenantUserSettingViewModel
                {
                    Id = tus.TenantUserSettingId,
                    Name = tus.SettingName,
                    Value = tus.SettingValue
                })
                .ToListAsync();
        }
        
        public async Task SaveUserSetting(TenantUserSettingViewModel viewModel, int tenantId, int userId)
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

        public async Task SaveAsync()
        {
            await context.SaveChangesAsync();
        }
        
        public async Task<User?> GetUserById(int id)
        {
            return await context.Users
                .Include(u => u.CurrentTenant)
                .FirstOrDefaultAsync(u => u.UserId == id);

        }

        public async Task<User?> GetUserByResetKey(string resetKey)
        {
            return await context.Users
                .Include(u => u.CurrentTenant)
                .FirstOrDefaultAsync(u => u.ResetKey.ToLower() == resetKey.ToLower());

        }

        public async Task<List<Tenant>> GetTenantsByUserIdAsync(int userId)
        {
            return await Queryable.Select(Queryable.Where(context.TenantUsers, tu => tu.UserId == userId), tu => tu.Tenant)
                .Distinct()
                .ToListAsync();
        }
        
        public async Task<bool> UpdateCurrentTenantIdAsync(int userId, int tenantId)
        {
            var user = await context.Users.FindAsync(userId);
        
            if (user == null)
            {
                return false;
            }

            // Check if the user is associated with the tenant
            var isAssociated = await context.TenantUsers
                .AnyAsync(tu => tu.UserId == userId && tu.TenantId == tenantId);

            if (!isAssociated)
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
                Log.Error(ex, $"Error updating Current Tenant. User {userId} Tenant {tenantId}");
                return false;
            }
        }
    }
    
}
