using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Hub.Services;
using Hub.ViewModels;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Hub.Controllers;

namespace Hub.Features.AgentAccess;

// Phase 1.5: the OAuth authorization endpoint. OpenIddict has already validated the client, redirect URI and
// PKCE by the time a request gets here; this decides who may connect an AI app and records their consent.
[AllowAnonymous]
public class AgentAccessAuthorizeController(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    AgentAccessMcpResource resource,
    AgentAccessClientCatalog catalog,
    IAgentGrantStore grants,
    TimeProvider time,
    IAntiforgery antiforgery) : Controller
{
    private const string CookieScheme = "Identity.Application";
    private const string TenantIdProperty = "tenant_id";

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var user = await ReadUserAsync();
        var application = await applications.FindByClientIdAsync(request.ClientId!)
                          ?? throw new InvalidOperationException("The client application cannot be found.");

        var existing = user?.TenantId is { } tenantId && !string.IsNullOrWhiteSpace(user.UserId)
            && await HasPermanentAuthorizationAsync(user.UserId, tenantId, application, request.GetScopes());

        var decision = AgentAccessAuthorizeDecider.Decide(
            user,
            new AgentAccessAuthorizeInput(
                request.GetScopes().ToList(),
                request.GetResources().ToList(),
                (request.Prompt ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries),
                existing),
            resource.Value);

        switch (decision.Outcome)
        {
            case AgentAccessAuthorizeOutcome.Reject:
                return Refuse(decision.Error!, decision.Description);

            case AgentAccessAuthorizeOutcome.LoginRequired:
                return Challenge(
                    new AuthenticationProperties { RedirectUri = Request.PathBase + Request.Path + Request.QueryString },
                    CookieScheme);

            case AgentAccessAuthorizeOutcome.Issue:
                return await IssueAsync(request, user!, application, createAuthorization: false);
        }

        if (Request.Method == HttpMethods.Post)
        {
            return await DecideAsync(request, user!, application);
        }

        ApplyConsentHeaders();
        return View("~/Features/AgentAccess/Views/Consent.cshtml", new AgentAccessConsentViewModel(
            await applications.GetDisplayNameAsync(application) ?? request.ClientId!,
            catalog.FindVendor(request.ClientId!)
            ?? (Uri.TryCreate(request.ClientId, UriKind.Absolute, out var clientUri) ? clientUri.Host : null),
            new Uri(resource.Value!).Host,
            request.GetScopes().ToList()));
    }

    private async Task<IActionResult> DecideAsync(OpenIddictRequest request, AgentAccessUser user, object application)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest();
        }

        var allowed = string.Equals(Request.Form["decision"], "allow", StringComparison.Ordinal);
        Log.Information(
            "Agent access consent {Decision} for client {ClientId}, user {UserId}, tenant {TenantId}",
            allowed ? "allowed" : "denied", request.ClientId, user.UserId, user.TenantId);

        return allowed
            ? await IssueAsync(request, user, application, createAuthorization: true)
            : Refuse(Errors.AccessDenied, "The user denied the request.");
    }

    private async Task<IActionResult> IssueAsync(OpenIddictRequest request, AgentAccessUser user, object application, bool createAuthorization)
    {
        var scopes = request.GetScopes().ToImmutableArray();
        var applicationId = (await applications.GetIdAsync(application))!;

        object? authorization;
        if (createAuthorization)
        {
            var descriptor = new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = applicationId,
                Subject = user.UserId,
                Type = AuthorizationTypes.Permanent,
                Status = Statuses.Valid,
                CreationDate = time.GetUtcNow()
            };
            descriptor.Scopes.UnionWith(scopes);
            descriptor.Properties[TenantIdProperty] = JsonSerializer.SerializeToElement(user.TenantId!.Value);
            descriptor.Properties["user_id"] = JsonSerializer.SerializeToElement(user.UserId);
            if (user.StaffId is { } staffId) descriptor.Properties["staff_id"] = JsonSerializer.SerializeToElement(staffId);
            if (user.ContactId is { } contactId) descriptor.Properties["contact_id"] = JsonSerializer.SerializeToElement(contactId);
            descriptor.Properties["client_type_id"] = JsonSerializer.SerializeToElement(user.ClientTypeId!.Value);
            authorization = await authorizations.CreateAsync(descriptor);
        }
        else
        {
            authorization = await FindPermanentAuthorizationAsync(user.UserId, user.TenantId!.Value, applicationId, scopes);
        }

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.UserId);
        identity.SetClaim(TenantIdProperty, user.TenantId!.Value.ToString());
        identity.SetScopes(scopes);
        identity.SetAudiences(resource.Value!, AgentAccessClientSeeder.ReservedTokenExchangeClientId);
        var authorizationId = (await authorizations.GetIdAsync(authorization!))!;
        identity.SetAuthorizationId(authorizationId);
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        await grants.MarkLiveAsync(authorizationId, HttpContext.RequestAborted);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<bool> HasPermanentAuthorizationAsync(string userId, int tenantId, object application, IEnumerable<string> scopes) =>
        await FindPermanentAuthorizationAsync(
            userId, tenantId, (await applications.GetIdAsync(application))!, scopes.ToImmutableArray()) is not null;

    // Authorizations live in the shared Master database, so a grant made against another tenant never counts.
    private async Task<object?> FindPermanentAuthorizationAsync(
        string userId, int tenantId, string applicationId, ImmutableArray<string> scopes)
    {
        await foreach (var candidate in authorizations.FindAsync(
                           userId, applicationId, Statuses.Valid, AuthorizationTypes.Permanent, scopes))
        {
            var properties = await authorizations.GetPropertiesAsync(candidate);
            if (properties.TryGetValue(TenantIdProperty, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var stored)
                && stored == tenantId)
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<AgentAccessUser?> ReadUserAsync()
    {
        var result = await HttpContext.AuthenticateAsync(CookieScheme);
        if (result.Principal?.Identity?.IsAuthenticated != true) return null;

        var principal = result.Principal;
        return new AgentAccessUser(
            principal.FindFirstValue("UserID") ?? string.Empty,
            Number(principal, "StaffID"),
            Number(principal, "ContactID"),
            Number(principal, "ClientTypeId"),
            Number(principal, "CurrentTenantID"));
    }

    private static int? Number(ClaimsPrincipal principal, string type) =>
        int.TryParse(principal.FindFirstValue(type), out var value) ? value : null;

    private IActionResult Refuse(string error, string? description) =>
        Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private void ApplyConsentHeaders()
    {
        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        Response.Headers.CacheControl = "no-store";
    }
}
