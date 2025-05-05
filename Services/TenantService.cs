using System.Collections.Generic;
using System.Threading.Tasks;
using Hub.Models.Master;
using Hub.Repositories;

namespace Hub.Services
{
    public interface ITenantService
    {
        Task<List<Tenant>> GetTenantsForUserAsync(int userId);
        string GetTenantLogoPath(string tenantCode);
    }

    public class TenantService(AuthenticationRepository authenticationRepository) : ITenantService
    {
        
        public async Task<List<Tenant>> GetTenantsForUserAsync(int userId)
        {
            return await authenticationRepository.GetTenantsByUserIdAsync(userId);
        }

        public string GetTenantLogoPath(string tenantCode)
        {

            if (string.IsNullOrEmpty(tenantCode))
                return "~/images/deliverDifferentLogo.png"; // Default logo
            
        
            var logoPath = $"~/images/{tenantCode}Logo.png";
        
            
            return logoPath;
        }
    }

}
