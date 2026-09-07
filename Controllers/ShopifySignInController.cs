using Hub.Interfaces;
using Hub.Services;
using Hub.Shared;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace Hub.Controllers;

/// <summary>
/// Shopify merchant sign-in: the credential check the shared front door cannot make for itself.
/// <para>
/// The old Urgent app asked a merchant for one thing after install - their dispatch username and
/// password - and that is what this restores, for any tenant. What the single-tenant app never had
/// to do is work out <em>which</em> courier, and <c>dbo.TenantUser</c> is the answer: signing in
/// says who the merchant is, and membership says whose merchant they are.
/// </para>
/// <para>
/// It lives in Hub rather than the front door because the front door is the public App URL. It is
/// granted no read of <c>dbo.[User]</c> or <c>dbo.[TenantUser]</c> at the database, deliberately, so
/// a compromise there does not hand over every dispatch password hash in the estate. What comes back
/// from here is a tenant to route to and a signed statement about who asked - never a credential.
/// </para>
/// <para>
/// Its own controller rather than a fifth method on <c>TenantsController</c>: that one is
/// tenant-scoped, and this needs different dependencies and a rate limit of its own.
/// </para>
/// </summary>
[Route("api/shopify")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class ShopifySignInController(
    IAuthenticationRepository authenticationRepository,
    IShopifyLinkTicketIssuer ticketIssuer) : Controller
{
    /// <summary>
    /// One answer for a user we do not have and a password that does not match. Distinguishing them
    /// would turn this into a way to enumerate every dispatch account in the estate.
    /// </summary>
    private const string Rejected = "That username and password do not match an account.";

    /// <summary>
    /// Burned when no user is found, so that "no such user" costs what "wrong password" costs. The
    /// salt is fixed and the result is discarded; only the elapsed time matters.
    /// </summary>
    private const string TimingSalt = "0000000000000000";

    [HttpPost("signin")]
    [EnableRateLimiting(RateLimitPolicies.ShopifyMerchantSignIn)]
    public async Task<IActionResult> SignIn(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] string? shop,
        [FromBody] ShopifySignInRequest? request)
    {
        if (!ServiceApiKey.IsValid(apiKey, ServiceApiKey.Caller.SetupService))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(shop))
        {
            return BadRequest(new { Message = "A shop domain is required." });
        }

        if (string.IsNullOrWhiteSpace(request?.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { Message = "A username and a password are both required." });
        }

        try
        {
            var user = await authenticationRepository.GetShopifyMerchantUserAsync(request.Username);

            if (user == null)
            {
                // Not a wasted hash. Returning here without one makes an unknown username measurably
                // faster than a known one, which is the whole of the enumeration attack.
                PasswordHelper.HashPassword(request.Password, TimingSalt);
                Log.Warning("Shopify sign-in rejected for {Username} on {Shop}: no such account",
                    request.Username, shop);
                return Unauthorized(new { Message = Rejected });
            }

            if (!PasswordHelper.Verify(request.Password, user.Salt, user.Password, user.IsLegacyHash))
            {
                Log.Warning("Shopify sign-in rejected for {Username} on {Shop}: wrong password",
                    request.Username, shop);
                return Unauthorized(new { Message = Rejected });
            }

            if (user.IsLegacyHash)
            {
                // The one moment the plaintext is in hand, and the only chance to move this row off
                // PBKDF2-SHA1 at 1,000 iterations.
                user.Password = PasswordHelper.HashPassword(request.Password, user.Salt);
                user.IsLegacyHash = false;
                await authenticationRepository.SaveAsync();
            }

            var tenants = await UsableTenantsAsync(user.UserId);

            return Ok(Answer(user.UserId, user.Email, shop, tenants));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shopify sign-in failed for {Username} on {Shop}", request.Username, shop);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The second call, for a merchant who belongs to more than one Shopify-enabled courier.
    /// <para>
    /// The selection ticket carries the authentication so the password is typed once and not held in
    /// the browser across the choice. It is not, however, the authority on what may be chosen:
    /// membership is re-read from master here, and the candidate list on the ticket only has to agree.
    /// </para>
    /// </summary>
    [HttpPost("select-tenant")]
    [EnableRateLimiting(RateLimitPolicies.ShopifyMerchantSignIn)]
    public async Task<IActionResult> SelectTenant(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] string? shop,
        [FromBody] ShopifySelectTenantRequest? request)
    {
        if (!ServiceApiKey.IsValid(apiKey, ServiceApiKey.Caller.SetupService))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(shop))
        {
            return BadRequest(new { Message = "A shop domain is required." });
        }

        var selection = ticketIssuer.ReadSelection(request?.SelectionTicket);

        if (selection == null)
        {
            return Unauthorized(new { Message = "That sign-in has expired. Please sign in again." });
        }

        // The ticket names the store it was issued for. Without this, the picker would be a way to
        // attach somebody else's shop to a courier this merchant happens to belong to.
        if (!string.Equals(selection.Shop, shop.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Shopify tenant selection for {Shop} presented a ticket issued for {TicketShop}",
                shop, selection.Shop);
            return Unauthorized(new { Message = "That sign-in has expired. Please sign in again." });
        }

        try
        {
            var tenantId = request!.TenantId;

            // Both, and in this order. The master check is the gate; the candidate check keeps the
            // answer consistent with what the merchant was actually shown.
            var belongs = await authenticationRepository.IsUserAssociatedWithTenantAsync(selection.UserId, tenantId);

            if (!belongs || !selection.TenantIds.Contains(tenantId))
            {
                Log.Warning("Shopify tenant selection refused: user {UserId} and tenant {TenantId} on {Shop}",
                    selection.UserId, tenantId, shop);
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { Message = "That courier is not one of yours." });
            }

            var tenants = await UsableTenantsAsync(selection.UserId);
            var chosen = tenants.FirstOrDefault(t => t.TenantId == tenantId);

            if (chosen == null)
            {
                // Switched off between the two calls, or its host row went bad. Our fault, not the
                // merchant's, and not a permissions answer.
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { Message = "That courier is not set up for Shopify yet." });
            }

            var ticket = ticketIssuer.IssueLink(selection.UserId, selection.Email, tenantId, shop.Trim());

            return Ok(new ShopifySignInResponse
            {
                Email = selection.Email,
                Tenants = [chosen],
                Tenant = chosen,
                LinkTicket = ticket.Token,
                LinkTicketExpiresAtUtc = ticket.ExpiresAtUtc
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shopify tenant selection failed for {Shop}", shop);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The couriers this user may connect a store to, with the unreachable ones removed.
    /// <para>
    /// The host check cannot happen in SQL - <see cref="Uri.IsWellFormedUriString"/> does not
    /// translate - and it is the same one the front door applies before routing, so a host that
    /// would fail there is not offered here.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<ShopifySignInTenant>> UsableTenantsAsync(int userId)
    {
        var tenants = await authenticationRepository.GetShopifyTenantsForUserAsync(userId);

        return
        [
            .. tenants
                .Select(t => new { Tenant = t, Host = UsableHost(t.Host) })
                .Where(x => x.Host != null)
                .Select(x => new ShopifySignInTenant
                {
                    TenantId = x.Tenant.TenantId,
                    Code = x.Tenant.Code,
                    Name = x.Tenant.Name,
                    Host = x.Host!
                })
        ];
    }

    /// <summary>
    /// Mirrors <c>MasterTenantDirectory.Usable</c> in the front door. Two copies, because neither
    /// solution compiles against the other; both are pinned by a test that names the other.
    /// </summary>
    private static string? UsableHost(string? host)
    {
        var trimmed = host?.Trim().TrimEnd('/');

        return string.IsNullOrWhiteSpace(trimmed) || !Uri.IsWellFormedUriString(trimmed, UriKind.Absolute)
            ? null
            : trimmed;
    }

    /// <summary>
    /// Nought, one or many, in one place so the three answers cannot drift apart.
    /// </summary>
    private ShopifySignInResponse Answer(int userId, string email, string shop,
        IReadOnlyList<ShopifySignInTenant> tenants)
    {
        var trimmedShop = shop.Trim();

        if (tenants.Count == 1)
        {
            var ticket = ticketIssuer.IssueLink(userId, email, tenants[0].TenantId, trimmedShop);

            return new ShopifySignInResponse
            {
                Email = email,
                Tenants = tenants,
                Tenant = tenants[0],
                LinkTicket = ticket.Token,
                LinkTicketExpiresAtUtc = ticket.ExpiresAtUtc
            };
        }

        return new ShopifySignInResponse
        {
            Email = email,
            Tenants = tenants,
            // Only where there is something to choose between. A merchant with no courier gets no
            // ticket at all, because there is nothing a ticket could authorise.
            SelectionTicket = tenants.Count > 1
                ? ticketIssuer.IssueSelection(userId, email, trimmedShop, [.. tenants.Select(t => t.TenantId)]).Token
                : null
        };
    }
}
