using Hub.Interfaces;
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
        var expectedKey = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey") ?? string.Empty;
        if (string.IsNullOrEmpty(expectedKey) || !string.Equals(apiKey, expectedKey, StringComparison.Ordinal))
        {
            Log.Warning("Partner directory request rejected: invalid or missing API key");
            return Unauthorized();
        }

        var listings = await partnerDirectoryService.GetActiveListingsAsync();
        return Ok(listings);
    }
}
