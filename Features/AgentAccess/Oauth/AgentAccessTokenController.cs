using System.Security.Claims;
using System.Text.Json;
using Hub.Features.AgentAccess.Grants;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.Oauth;

// Phases 1.5, 1.6 and 1.9: the OAuth token endpoint. It answers the authorization-code exchange, the refresh grant, and
// the token exchange the MCP server uses to turn an agent pass into a short-lived app pass. A refresh or an exchange is
// where a grant is checked again: still valid, under twelve months old, tenant still on, and the person behind it still
// able to connect AI apps.
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class AgentAccessTokenController(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IAgentGrantStore grants,
    IAgentPassIdentityLoader identities,
    IAgentSubjectTokenValidator subjectTokens,
    TimeProvider time) : Controller
{
    private const string AppPassAudience = "dfrnt-apps";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
    private static readonly TimeSpan AppPassLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(5);

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

        if (string.Equals(request.GrantType, AgentAccessClientSeeder.TokenExchangeGrantType, StringComparison.Ordinal))
        {
            return await TokenExchangeAsync(request);
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

        var evaluation = await EvaluateAsync(authorization, authorizationId!, cancellationToken);
        if (evaluation.Decision.Verdict == AgentGrantVerdict.Refuse) return Refuse(evaluation.Decision.Error!, evaluation.Decision.Description);

        await grants.MarkLiveAsync(authorizationId!, cancellationToken);

        // Only what was originally approved: the scope parameter on a refresh can narrow it, never widen it.
        var identity = new ClaimsIdentity(
            principal.Claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.SetScopes(principal.GetScopes());
        identity.SetAudiences(principal.GetAudiences());
        identity.SetAuthorizationId(authorizationId!);
        identity.SetDestinations(_ => [Destinations.AccessToken]);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // RFC 8693, for the MCP server only: an agent pass in, a 15 minute app pass out for the Despatch apps.
    private async Task<IActionResult> TokenExchangeAsync(OpenIddictRequest request)
    {
        var cancellationToken = HttpContext.RequestAborted;

        if (!string.Equals(request.ClientId, AgentAccessClientSeeder.ReservedTokenExchangeClientId, StringComparison.Ordinal))
        {
            return Refuse(Errors.UnauthorizedClient, "Only the MCP server may exchange tokens.");
        }

        var subjectToken = request.GetParameter("subject_token")?.ToString();
        var subjectType = request.GetParameter("subject_token_type")?.ToString();
        if (string.IsNullOrEmpty(subjectToken) || !string.Equals(subjectType, AccessTokenType, StringComparison.Ordinal))
        {
            return Refuse(Errors.InvalidRequest, "A subject_token of type access_token is required.");
        }

        var validated = await subjectTokens.ValidateAsync(subjectToken);
        if (validated.Token is not { } subject)
        {
            return Refuse(Errors.InvalidGrant, validated.Error);
        }

        var authorization = await authorizations.FindByIdAsync(subject.AuthorizationId, cancellationToken);
        if (authorization is null)
        {
            return Refuse(Errors.InvalidGrant, "The authorization cannot be found.");
        }

        var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken);
        if (!TryReadIdentity(properties, out var tenantId, out var userId, out _)
            || tenantId != subject.TenantId
            || !string.Equals(userId, subject.Subject, StringComparison.Ordinal))
        {
            return Refuse(Errors.InvalidGrant, "The subject token does not match its authorization.");
        }

        var evaluation = await EvaluateAsync(authorization, subject.AuthorizationId, cancellationToken);
        if (evaluation.Decision.Verdict == AgentGrantVerdict.Refuse) return Refuse(evaluation.Decision.Error!, evaluation.Decision.Description);

        await grants.MarkLiveAsync(subject.AuthorizationId, cancellationToken);
        await TouchLastUsedAsync(authorization, properties, cancellationToken);

        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.AddClaims(AgentPassClaims.Build(evaluation.Identity!));
        identity.SetClaim(Claims.Subject, subject.Subject);
        identity.AddClaim(new Claim("grant_id", subject.AuthorizationId));
        identity.AddClaim(new Claim("agent", await ClientIdOfAsync(authorization, cancellationToken)));
        identity.AddClaim(new Claim("auth_kind", "agent"));
        identity.AddClaim(new Claim("act", JsonSerializer.Serialize(new { client_id = AgentAccessClientSeeder.ReservedTokenExchangeClientId }), "JSON"));
        identity.SetScopes("read");
        identity.SetAudiences(AppPassAudience);
        identity.SetAccessTokenLifetime(AppPassLifetime);
        identity.SetDestinations(_ => [Destinations.AccessToken]);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private sealed record GrantEvaluation(AgentGrantDecision Decision, AgentPassIdentity? Identity);

    // The checks every refresh and every exchange pass: valid, under twelve months, tenant on, person still eligible.
    // A grant that fails for good is revoked here, together with its Redis key.
    private async Task<GrantEvaluation> EvaluateAsync(
        object authorization, string authorizationId, CancellationToken cancellationToken)
    {
        var status = await authorizations.GetStatusAsync(authorization, cancellationToken);
        var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken);
        if (!TryReadIdentity(properties, out var tenantId, out var userId, out var contactId))
        {
            await Revoke(authorization, authorizationId, cancellationToken);
            return new GrantEvaluation(AgentGrantDecision.Refused("The authorization is incomplete; approve the app again."), null);
        }

        var valid = string.Equals(status, Statuses.Valid, StringComparison.Ordinal);
        var load = valid
            ? await identities.LoadAsync(tenantId, userId, contactId, cancellationToken)
            : new AgentPassLoad(AgentGrantEligibility.Eligible, null);

        var state = new AgentGrantState(
            status,
            await authorizations.GetCreationDateAsync(authorization, cancellationToken),
            valid && await grants.IsTenantOffAsync(tenantId, cancellationToken),
            load.Eligibility);

        var decision = AgentGrantPolicy.Decide(state, time.GetUtcNow());
        if (decision.Verdict == AgentGrantVerdict.Refuse)
        {
            if (decision.RevokeGrant) await Revoke(authorization, authorizationId, cancellationToken);

            Log.Information(
                "Agent access grant {AuthorizationId} refused for tenant {TenantId}: {Reason}",
                authorizationId, tenantId, decision.Description);
        }

        return new GrantEvaluation(decision, load.Identity);
    }

    private async Task TouchLastUsedAsync(
        object authorization, IReadOnlyDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        try
        {
            var now = time.GetUtcNow();
            if (properties.TryGetValue("last_used_utc", out var previous)
                && DateTimeOffset.TryParse(previous.GetString(), out var last)
                && now - last < LastUsedThrottle)
            {
                return;
            }

            var descriptor = new OpenIddictAuthorizationDescriptor();
            await authorizations.PopulateAsync(descriptor, authorization, cancellationToken);
            descriptor.Properties["last_used_utc"] = JsonSerializer.SerializeToElement(now.ToString("O"));
            await authorizations.UpdateAsync(authorization, descriptor, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Agent access could not record last use");
        }
    }

    private async Task<string> ClientIdOfAsync(object authorization, CancellationToken cancellationToken)
    {
        var applicationId = await authorizations.GetApplicationIdAsync(authorization, cancellationToken);
        var application = applicationId is null ? null : await applications.FindByIdAsync(applicationId, cancellationToken);
        return (application is null ? null : await applications.GetClientIdAsync(application, cancellationToken)) ?? "unknown";
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
