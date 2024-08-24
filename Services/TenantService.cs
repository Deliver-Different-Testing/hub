using System.Collections.Generic;
using System.Threading.Tasks;
using UrgentHub.Models.Master;
using UrgentHub.Repositories;

namespace UrgentHub.Services
{
    public interface ITenantService
    {
        Task<List<Tenant>> GetTenantsForUserAsync(int userId);
    }

    public class TenantService(AuthenticationRepository authenticationRepository) : ITenantService
    {
        
        public async Task<List<Tenant>> GetTenantsForUserAsync(int userId)
        {
            return await authenticationRepository.GetTenantsByUserIdAsync(userId);
        }
    }
}
