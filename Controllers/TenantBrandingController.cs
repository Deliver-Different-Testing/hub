using Hub.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Controllers;

[Route("api/tenant/{tenantId:int}")]
[AllowAnonymous]
[EnableRateLimiting("api")]
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
