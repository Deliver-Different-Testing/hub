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
    public async Task<IActionResult> GetActiveListings(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] int? tenantId = null)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var listings = await partnerDirectoryService.GetActiveListingsAsync(tenantId);
            return Ok(listings);
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
            return Ok(listing);
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
            return Ok(result);
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
            return Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update partner directory listing for tenant {TenantId}", tenantId);
            return StatusCode(500);
        }
    }

    [HttpPatch("{tenantId:int}/activate")]
    public async Task<IActionResult> ActivateListing(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var activated = await partnerDirectoryService.ActivateListingAsync(tenantId);
            return !activated
                ? throw new InvalidOperationException(
                    "Failed to activate partner directory listing for tenant {TenantId}")
                : Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to activate partner directory listing for tenant {TenantId}", tenantId);
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

    [HttpPost("link-requests")]
    public async Task<IActionResult> CreateLinkRequest(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] LinkRequestCreateRequest request)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var result = await partnerDirectoryService.CreateLinkRequestAsync(request);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException)
        {
            return Conflict();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create link request from tenant {RequestingTenantId} to {TargetTenantId}",
                request.RequestingTenantId, request.TargetTenantId);
            return StatusCode(500);
        }
    }

    [HttpGet("link-requests")]
    public async Task<IActionResult> GetLinkRequests(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] int tenantId)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var results = await partnerDirectoryService.GetLinkRequestsAsync(tenantId);
            return Ok(results);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get link requests for tenant {TenantId}", tenantId);
            return StatusCode(500);
        }
    }

    [HttpPost("link-requests/{requestId:int}/accept")]
    public async Task<IActionResult> AcceptLinkRequest(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int requestId)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var result = await partnerDirectoryService.AcceptLinkRequestAsync(requestId);
            return result == null ? NotFound() : Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to accept link request {RequestId}", requestId);
            return StatusCode(500);
        }
    }

    [HttpPost("link-requests/{requestId:int}/decline")]
    public async Task<IActionResult> DeclineLinkRequest(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int requestId,
        [FromBody] DeclineLinkRequestRequest? request)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        try
        {
            var result = await partnerDirectoryService.DeclineLinkRequestAsync(requestId, request?.Reason);
            return result == null ? NotFound() : Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to decline link request {RequestId}", requestId);
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