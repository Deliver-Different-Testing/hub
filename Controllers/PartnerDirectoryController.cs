using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace Hub.Controllers;

[Route("api/partner-directory")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class PartnerDirectoryController(IPartnerDirectoryService partnerDirectoryService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetActiveListings([FromHeader(Name = "X-Api-Key")] string? apiKey)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var listings = await partnerDirectoryService.GetActiveListingsAsync();
            return Json(listings);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get active partner directory listings");
            return StatusCode(500);
        }
    }

    [HttpGet("{tenantId:int}")]
    public async Task<IActionResult> GetListing(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var listing = await partnerDirectoryService.GetListingAsync(tenantId);
            return Json(listing);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get partner directory listing for tenant {TenantId}", tenantId);
            return StatusCode(500);
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddListing(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] PartnerDirectoryListingRequest request)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var result = await partnerDirectoryService.AddListingAsync(request);
            return Json(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add partner directory listing");
            return StatusCode(500);
        }
    }

    [HttpPut("{tenantId:int}")]
    public async Task<IActionResult> UpdateListing(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId,
        [FromBody] PartnerDirectoryListingRequest request)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var result = await partnerDirectoryService.UpdateListingAsync(tenantId, request);
            return Json(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update partner directory listing for tenant {TenantId}", tenantId);
            return StatusCode(500);
        }
    }

    [HttpDelete("{tenantId:int}")]
    public async Task<IActionResult> RemoveListing(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var removed = await partnerDirectoryService.RemoveListingAsync(tenantId);
            return !removed
                ? throw new InvalidOperationException(
                    "Failed to remove partner directory listing for tenant {TenantId}")
                : Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove partner directory listing for tenant {TenantId}", tenantId);
            return StatusCode(500);
        }
    }

    private static bool IsApiKeyValid(string? apiKey)
    {
        var expectedKey = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey") ?? string.Empty;
        if (!string.IsNullOrEmpty(expectedKey) &&
            string.Equals(apiKey, expectedKey, StringComparison.Ordinal)) return true;
        Log.Warning("Partner directory request rejected: invalid or missing API key");
        return false;
    }
}