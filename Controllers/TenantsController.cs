using Hub.Interfaces;
using Hub.Shared;
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

    /// <summary>
    /// Which tenant owns a Shopify shop.
    /// <para>
    /// The Deliver DFRNT app is a single Shopify listing with one App URL, so install, the
    /// <c>/api/Rates</c> carrier callback and every webhook arrive at one shared Integration Manager
    /// deployment serving all tenants. The shop domain is the only tenant identity those requests
    /// carry, and IM holds no master-controller connection, so it asks here.
    /// </para>
    /// <para>
    /// The shop is a query parameter rather than a route segment on purpose: a shop domain ends in
    /// <c>.myshopify.com</c>, and a final route segment containing dots is the case where IIS and
    /// the static-file handler take the request before MVC sees it.
    /// </para>
    /// </summary>
    [HttpGet("by-shopify-shop")]
    public async Task<IActionResult> GetByShopifyShop(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] string? shop)
    {
        if (!IsApiKeyValid(apiKey, ServiceApiKey.Caller.SetupService))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(shop))
        {
            return BadRequest(new { Message = "A shop domain is required." });
        }

        try
        {
            var mapping = await authenticationRepository.GetTenantByShopifyShopAsync(shop);

            // Not an error. A public app URL gets asked about shops that never installed, or
            // uninstalled long ago; the caller turns this into a rejected request.
            return mapping == null ? NotFound() : Ok(mapping);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to resolve the tenant for Shopify shop {Shop}", shop);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Records that a Shopify shop belongs to a tenant. Idempotent for the same tenant, so a merchant
    /// signing in again after a failed hand-over is not told something went wrong; a shop already
    /// recorded against a <em>different</em> tenant is a conflict rather than an update, because
    /// re-pointing it would move a live merchant's orders into another courier's database.
    /// </summary>
    [HttpPut("{tenantId:int}/shopify-shop")]
    public async Task<IActionResult> PutShopifyShop(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId,
        [FromBody] ShopifyShopTenantRequest request)
    {
        if (!IsApiKeyValid(apiKey))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.Shop))
        {
            return BadRequest(new { Message = "A shop domain is required." });
        }

        try
        {
            return await authenticationRepository.MapShopifyShopAsync(request.Shop, tenantId) switch
            {
                ShopifyShopMappingResult.Mapped or ShopifyShopMappingResult.AlreadyMapped => NoContent(),
                ShopifyShopMappingResult.TenantNotFound => NotFound(),
                _ => Conflict(new
                {
                    Message = $"'{request.Shop}' is already mapped to a different tenant."
                })
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to map Shopify shop {Shop} to tenant {TenantId}", request.Shop, tenantId);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Detaches a shop from a tenant.
    /// <para>
    /// Built for Shopify requires a merchant be able to disconnect a third-party system from inside
    /// the embedded app, so this is reachable by the merchant's own store rather than only by staff.
    /// The tenant is in the route and the mapping must belong to it: a delete keyed on the shop
    /// alone would let the shared deployment detach any store on any tenant's behalf.
    /// </para>
    /// </summary>
    [HttpDelete("{tenantId:int}/shopify-shop")]
    public async Task<IActionResult> UnmapShopifyShop(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        int tenantId,
        [FromQuery] string? shop)
    {
        if (!IsApiKeyValid(apiKey, ServiceApiKey.Caller.SetupService))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(shop))
        {
            return BadRequest(new { Message = "A shop domain is required." });
        }

        try
        {
            // NoContent either way: a merchant clicking disconnect twice, or on a store that was
            // already detached, has got what they asked for.
            await authenticationRepository.UnmapShopifyShopAsync(shop, tenantId);
            return NoContent();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to unmap Shopify shop {Shop} from tenant {TenantId}", shop, tenantId);
            return StatusCode(500);
        }
    }

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
    [HttpPut("{tenantId:int}/shopify-host")]
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
    private static bool IsApiKeyValid(string? apiKey, ServiceApiKey.Caller allowed = ServiceApiKey.Caller.Trusted) =>
        ServiceApiKey.IsValid(apiKey, allowed);
}
