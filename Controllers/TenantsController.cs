using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace Hub.Controllers;

[Route("api/tenants")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class TenantsController(IAuthenticationRepository authenticationRepository) : Controller
{
    [HttpGet("{tenantId:int}/connection-string")]
    public async Task<IActionResult> GetConnectionString(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId)
    {
        if (!IsApiKeyValid(apiKey))
        {
            return Unauthorized();
        }

        try
        {
            var connectionString = await authenticationRepository.GetTenantConnectionStringAsync(tenantId);
            if (string.IsNullOrEmpty(connectionString))
            {
                return NotFound();
            }

            return Ok(new TenantConnectionStringResponse
            {
                TenantId = tenantId,
                ConnectionString = connectionString
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get connection string for tenant {TenantId}", tenantId);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The tenant's wall-clock timezone. Integration Manager needs this to evaluate the Shopify
    /// booking rules that run in its order-polling sweep, and reads it here so it does not need a
    /// master-controller connection of its own.
    /// </summary>
    [HttpGet("{tenantId:int}/time-zone")]
    public async Task<IActionResult> GetTimeZone(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId)
    {
        if (!IsApiKeyValid(apiKey))
        {
            return Unauthorized();
        }

        try
        {
            var timeZone = await authenticationRepository.GetTenantTimeZoneAsync(tenantId);
            if (string.IsNullOrEmpty(timeZone))
            {
                return NotFound();
            }

            return Ok(new TenantTimeZoneResponse
            {
                TenantId = tenantId,
                TimeZone = timeZone
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get timezone for tenant {TenantId}", tenantId);
            return StatusCode(500);
        }
    }

    private static bool IsApiKeyValid(string? apiKey)
    {
        var expectedKey = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey") ?? string.Empty;
        if (!string.IsNullOrEmpty(expectedKey) &&
            string.Equals(apiKey, expectedKey, StringComparison.Ordinal))
        {
            return true;
        }

        Log.Warning("Tenant metadata request rejected: invalid or missing API key");
        return false;
    }
}
