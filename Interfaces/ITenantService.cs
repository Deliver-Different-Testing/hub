using Hub.Models.Master;

namespace Hub.Interfaces;

public interface ITenantService
{
    Task<IReadOnlyList<Tenant>> GetTenantsForUserAsync(int userId);
}