using System.Threading.Tasks;
using Hub.ViewModels;

namespace Hub.Services;

public interface ITenantBrandingConfigService
{
    Task<TenantBrandingResponse> GetReportConfigAsync(int tenantId);
}
