using Hub.Interfaces;
using Hub.Shared;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace Hub.Controllers;

[Route("api/tenants/{tenantId:int}")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class TenantsController(IAuthenticationRepository authenticationRepository) : Controller
{
    [HttpGet("connection-string")]
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
    [HttpGet("time-zone")]
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

    // Gone with the front door's move to reading master directly: GET by-shopify-shop, and PUT and
    // DELETE {tenantId:int}/shopify-shop. Hub answered "which tenant owns this shop" and recorded
    // the pairing while Integration Manager was one shared deployment with no master connection of
    // its own. The front door holds that connection now - a login granted the shop map and
    // dbo.[User], and denied Tenant.DBConnection - so it reads and writes those rows itself and
    // nothing called these. The table and its entity stay; only the endpoints went.

    /// <summary>
    /// Records where a courier's Integration Manager lives, and by doing so switches Shopify on for
    /// them: a merchant signing in is only offered couriers that have a row here.
    /// <para>
    /// Called by the deployment itself at startup, because it is the only thing that knows its own
    /// public address. It was once described as a side effect of issuing a pairing code — which
    /// nothing ever implemented, and which is why the table sat empty.
    /// </para>
    /// <para>
    /// Trusted tier only. The front door reads this table to route a merchant; letting the most
    /// exposed service in the estate write it would let it point a courier's merchants elsewhere.
    /// </para>
    /// </summary>
    [HttpPut("shopify-host")]
    public async Task<IActionResult> PutShopifyHost(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId,
        [FromBody] ShopifyTenantHostRequest? request)
    {
        if (!IsApiKeyValid(apiKey))
        {
            return Unauthorized();
        }

        // Trailing slash off here, so the stored string is the one the front door compares and
        // neither side has to normalise the other's.
        var url = request?.IntegrationManagerUrl?.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest(new { Message = "An Integration Manager URL is required." });
        }

        // Refused now rather than filtered out at read time: a host that cannot be routed to is a
        // courier switched on in name only, and this is the last moment anyone can be told.
        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            return BadRequest(new { Message = "That is not an absolute URL." });
        }

        try
        {
            return await authenticationRepository.UpsertShopifyTenantHostAsync(tenantId, url)
                ? NoContent()
                : NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to record the Shopify host for tenant {TenantId}", tenantId);
            return StatusCode(500);
        }
    }

    // The key check itself moved to Hub.Shared.ServiceApiKey when Shopify merchant sign-in became a
    // second controller needing the same two tiers. Aliased rather than rewritten at nine call sites,
    // so this file's diff stays about what left it rather than about punctuation.
    private static bool IsApiKeyValid(string? apiKey) => ServiceApiKey.IsValid(apiKey);
}
