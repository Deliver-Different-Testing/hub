using System.Threading.Tasks;
using Hub.ViewModels;

namespace Hub.Services;

public interface ITenantBrandingConfigService
{
    Task<TenantBrandingResponse> GetReportConfigAsync(int tenantId);
    Task<bool> UserHasAccessToTenantAsync(int userId, int tenantId);
}
