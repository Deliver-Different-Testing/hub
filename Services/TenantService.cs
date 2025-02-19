using System.Collections.Generic;
using System.Threading.Tasks;
using Hub.Models.Master;
using Hub.Repositories;

namespace Hub.Services
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
