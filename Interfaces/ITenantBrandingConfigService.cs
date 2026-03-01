using System.Threading.Tasks;
using Hub.ViewModels;

namespace Hub.Interfaces;

public interface ITenantBrandingConfigService
{
    Task<TenantBrandingResponse> GetReportConfigAsync(int tenantId);
}
