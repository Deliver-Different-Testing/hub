using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess;

// Phase 1.5 and 1.6: the OAuth token endpoint. The authorization-code exchange and the refresh grant are answered
// here; the app-pass exchange arrives in 1.9. A refresh is where a grant is checked again: still valid, under twelve
// months old, tenant still on, and the person behind it still able to connect AI apps.
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class AgentAccessTokenController(
    IOpenIddictAuthorizationManager authorizations,
    IAgentGrantStore grants,
    IAgentGrantEligibilityChecker eligibility,
    TimeProvider time) : Controller
{
    [HttpPost("~/connect/token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (result.Principal?.GetAuthorizationId() is { } codeAuthorizationId)
            {
                await grants.MarkLiveAsync(codeAuthorizationId, HttpContext.RequestAborted);
            }

            return SignIn(result.Principal!, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await RefreshAsync();
        }

        return Refuse(Errors.UnsupportedGrantType, "This grant type is not supported.");
    }

    private async Task<IActionResult> RefreshAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal!;

        var authorizationId = principal.GetAuthorizationId();
        var authorization = authorizationId is null ? null : await authorizations.FindByIdAsync(authorizationId, cancellationToken);
        if (authorization is null)
        {
            return Refuse(Errors.InvalidGrant, "The authorization cannot be found.");
        }

        var status = await authorizations.GetStatusAsync(authorization, cancellationToken);
        var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken);
        if (!TryReadIdentity(properties, out var tenantId, out var userId, out var contactId))
        {
            await Revoke(authorization, authorizationId!, cancellationToken);
            return Refuse(Errors.InvalidGrant, "The authorization is incomplete; approve the app again.");
        }

        var valid = string.Equals(status, Statuses.Valid, StringComparison.Ordinal);
        var state = new AgentGrantState(
            status,
            await authorizations.GetCreationDateAsync(authorization, cancellationToken),
            valid && await grants.IsTenantOffAsync(tenantId, cancellationToken),
            valid ? await eligibility.CheckAsync(tenantId, userId, contactId, cancellationToken) : AgentGrantEligibility.Eligible);

        var decision = AgentGrantPolicy.Decide(state, time.GetUtcNow());
        if (decision.Verdict == AgentGrantVerdict.Refuse)
        {
            if (decision.RevokeGrant) await Revoke(authorization, authorizationId!, cancellationToken);

            Log.Information(
                "Agent access refresh refused for authorization {AuthorizationId}, tenant {TenantId}: {Reason}",
                authorizationId, tenantId, decision.Description);
            return Refuse(decision.Error!, decision.Description);
        }

        await grants.MarkLiveAsync(authorizationId!, cancellationToken);

        // Only what was originally approved: the scope parameter on a refresh can narrow it, never widen it.
        var identity = new ClaimsIdentity(
            principal.Claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.SetScopes(principal.GetScopes());
        identity.SetResources(principal.GetResources());
        identity.SetAuthorizationId(authorizationId!);
        identity.SetDestinations(_ => [Destinations.AccessToken]);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task Revoke(object authorization, string authorizationId, CancellationToken cancellationToken)
    {
        await authorizations.TryRevokeAsync(authorization, cancellationToken);
        await grants.RevokeAsync(authorizationId, cancellationToken);
    }

    private static bool TryReadIdentity(
        IReadOnlyDictionary<string, JsonElement> properties, out int tenantId, out string userId, out int contactId)
    {
        tenantId = 0;
        userId = string.Empty;
        contactId = 0;

        return properties.TryGetValue("tenant_id", out var tenant) && tenant.ValueKind == JsonValueKind.Number && tenant.TryGetInt32(out tenantId)
               && properties.TryGetValue("user_id", out var user) && user.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(userId = user.GetString()!)
               && properties.TryGetValue("contact_id", out var contact) && contact.ValueKind == JsonValueKind.Number && contact.TryGetInt32(out contactId);
    }

    private IActionResult Refuse(string error, string? description) =>
        Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
