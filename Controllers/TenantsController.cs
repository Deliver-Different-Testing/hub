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

    private static bool IsApiKeyValid(string? apiKey)
    {
        var expectedKey = Environment.GetEnvironmentVariable("PartnerDirectoryApiKey") ?? string.Empty;
        if (!string.IsNullOrEmpty(expectedKey) &&
            string.Equals(apiKey, expectedKey, StringComparison.Ordinal))
        {
            return true;
        }

        Log.Warning("Tenant connection-string request rejected: invalid or missing API key");
        return false;
    }
}
