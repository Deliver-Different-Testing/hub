#nullable enable
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using UrgentHub.Models.Master;
using UrgentHub.Models;

namespace UrgentHub.Repositories
{

    public class AuthenticationRepository(MasterContext context)
    {
        
        public async Task<User?> GetUserByEmail(string email)
        {
            return await context.Users
                .Include(u => u.CurrentTenant)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

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
            return await context.TenantUsers
                .Where(tu => tu.UserId == userId)
                .Select(tu => tu.Tenant)
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
