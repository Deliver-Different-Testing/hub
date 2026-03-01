using System.Collections.Generic;
using System.Threading.Tasks;
using Hub.Models.Master;

namespace Hub.Interfaces;

public interface ITenantService
{
    Task<List<Tenant>> GetTenantsForUserAsync(int userId);
}