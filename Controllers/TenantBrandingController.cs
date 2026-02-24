using System.Threading.Tasks;
using Hub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers;

[Route("api/tenant/{tenantId:int}")]
[AllowAnonymous]
public class TenantBrandingController(ITenantBrandingConfigService tenantBrandingConfigService) : Controller
{
    [HttpGet("report-config")]
    public async Task<IActionResult> GetReportConfig(int tenantId)
    {
        var config = await tenantBrandingConfigService.GetReportConfigAsync(tenantId);
        if (config == null) return NotFound();

        return Ok(config);
    }
}
