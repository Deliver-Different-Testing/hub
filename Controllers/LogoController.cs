using System;
using System.Threading.Tasks;
using Hub.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Controllers;

[Route("api/[controller]")]
public class LogoController(ITenantLogoService tenantLogoService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetLogo()
    {
        try
        {
            var logoUrl = await tenantLogoService.GetLogoUrlAsync();

            // If it's a local path, serve the file directly
            if (logoUrl != null && logoUrl.StartsWith("/"))
            {
                return Json(new { success = true, logoUrl = logoUrl, isLocal = true });
            }

            // If it's an S3 URL, return the pre-signed URL
            return Json(new { success = true, logoUrl = logoUrl, isLocal = false });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error retrieving logo", logoUrl = "/images/deliverDifferentLogo.png" });
        }
    }

    [HttpGet("exists")]
    public async Task<IActionResult> LogoExists()
    {
        try
        {
            var exists = await tenantLogoService.LogoExistsAsync();
            return Json(new { success = true, exists = exists });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, exists = false });
        }
    }

    [HttpPost("clear-cache")]
    public IActionResult ClearCache()
    {
        try
        {
            tenantLogoService.ClearCache();
            return Json(new { success = true, message = "Cache cleared successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error clearing cache" });
        }
    }
}
