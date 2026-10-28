using Hub.Features.Branding;
using Hub.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Controllers;

[Route("api/tenant/{tenantId:int}")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class TenantBrandingController(
    ITenantBrandingConfigService tenantBrandingConfigService,
    ITenantAppearanceService tenantAppearanceService) : Controller
{
    [HttpGet("report-config")]
    public async Task<IActionResult> GetReportConfig(int tenantId)
    {
        var config = await tenantBrandingConfigService.GetReportConfigAsync(tenantId);
        if (config == null)
        {
            return NotFound();
        }

        return Ok(config);
    }

    [HttpGet("appearance")]
    public async Task<IActionResult> GetAppearance(int tenantId, CancellationToken cancellationToken)
    {
        var lookup = await tenantAppearanceService.GetAsync(tenantId, cancellationToken);
        return lookup.Status switch
        {
            TenantAppearanceStatus.TenantNotFound => NotFound(),
            TenantAppearanceStatus.Found => Ok(lookup.Appearance),
            _ => NoContent()
        };
    }
}
