using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Hub.Features.AgentAccess.Gate;
using Hub.Features.AgentAccess.Grants;
using Hub.Interfaces;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.Oauth;

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
    IAntiforgery antiforgery,
    IAgentAccessTenantResolver tenants,
    IAuthenticationRepository master,
    IAccountService accounts,
    AgentAccessConnectTickets tickets,
    IAgentPassIdentityLoader identities,
    IOptions<AntiforgeryOptions> antiforgeryOptions) : Controller
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
        var hubTenant = await tenants.ResolveAsync(HttpContext.RequestAborted);
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
            resource.Value,
            hubTenant?.TenantId);

        switch (decision.Outcome)
        {
            case AgentAccessAuthorizeOutcome.Reject:
                return Refuse(decision.Error!, decision.Description);

            case AgentAccessAuthorizeOutcome.LoginRequired:
                return Challenge(
                    new AuthenticationProperties { RedirectUri = Request.PathBase + Request.Path + Request.QueryString },
                    CookieScheme);

            case AgentAccessAuthorizeOutcome.TenantSwitchRequired:
                return await SwitchTenantAsync(request, user!, application, hubTenant!);
        }

        // Only someone who would get a pass may connect: the same check every refresh and exchange makes, including the
        // role's agent-connect verb in Access Control. Asked before consent, and again when an earlier consent is reused.
        if (await PassRefusalAsync(user!) is { } refusal)
        {
            return refusal;
        }

        if (decision.Outcome == AgentAccessAuthorizeOutcome.Issue)
        {
            return await IssueAsync(request, user!, application, createAuthorization: false);
        }

        if (Request.Method == HttpMethods.Post)
        {
            return await DecideAsync(request, user!, application);
        }

        return await ToConnectPageAsync(request, user!, application, AgentAccessConnectTicket.Consent);
    }

    // The consent and switch pages are SPA routes. What they show travels in a sealed ticket rather than the page's URL,
    // and the original request rides along so the page's form can post it back here for OpenIddict to validate again.
    private async Task<IActionResult> ToConnectPageAsync(
        OpenIddictRequest request, AgentAccessUser user, object application, string kind,
        string? currentOrganisation = null, string? hubOrganisation = null, bool canSwitch = false, string? error = null)
    {
        var clientId = request.ClientId!;
        var clientUri = Uri.TryCreate(clientId, UriKind.Absolute, out var parsed) ? parsed : null;
        var ticket = new AgentAccessConnectTicket(
            kind,
            user.UserId,
            user.TenantId!.Value,
            OriginalParameters().ToList(),
            await applications.GetDisplayNameAsync(application) ?? clientId,
            catalog.FindVendor(clientId),
            // A metadata document's name is the app's own claim (CIMD section 10.10), so the page names the host it came
            // from alongside it. Only allow-listed hosts get this far (ClientMetadataService).
            clientUri?.Host,
            new Uri(resource.Value!).Host,
            Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var redirect) ? redirect.Host : null,
            currentOrganisation,
            hubOrganisation,
            canSwitch,
            error);

        return Redirect(Request.PathBase + AgentAccessExtensions.PagePathPrefix + "/connect?ticket="
                        + Uri.EscapeDataString(tickets.Protect(ticket)));
    }

    // The authorization request as it arrived: the query on the first visit, the form once the page has posted it back.
    private IEnumerable<AgentAccessParameter> OriginalParameters() =>
        (Request.HasFormContentType ? Request.Form.AsEnumerable() : Request.Query.AsEnumerable())
        .Where(p => p.Key != "decision" && p.Key != antiforgeryOptions.Value.FormFieldName)
        .SelectMany(p => p.Value.Select(v => new AgentAccessParameter(p.Key, v ?? string.Empty)));

    // Only DFRNT staff belong to more than one tenant. They are told which tenant they are signed in to and offered this
    // Hub's; the switch is the one Hub's own organisation picker makes, and the authorization then starts again with the
    // new cookie. Someone who does not belong to this Hub's tenant is not offered one.
    private async Task<IActionResult> SwitchTenantAsync(
        OpenIddictRequest request, AgentAccessUser user, object application, AgentAccessTenant hubTenant)
    {
        var member = int.TryParse(user.UserId, out var userId)
                     && await master.IsUserAssociatedWithTenantAsync(userId, hubTenant.TenantId);
        string? error = null;

        if (Request.Method == HttpMethods.Post)
        {
            try
            {
                await antiforgery.ValidateRequestAsync(HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return BadRequest();
            }

            if (!string.Equals(Request.Form["decision"], "switch", StringComparison.Ordinal))
            {
                return Refuse(Errors.AccessDenied, "The user did not switch tenant.");
            }

            if (member)
            {
                // The result's redirect URL is the hop to the tenant's own Hub, which is this one, so it is not followed.
                var result = await accounts.UpdateCurrentTenantAsync(HttpContext, hubTenant.TenantId);
                if (result.Success)
                {
                    Log.Information(
                        "Agent access: user {UserId} switched from tenant {FromTenantId} to {ToTenantId} to connect {ClientId}",
                        user.UserId, user.TenantId, hubTenant.TenantId, request.ClientId);
                    return Redirect(AuthorizeUrlFromForm());
                }

                error = result.Message ?? "Couldn't switch organisation. Try again.";
            }
        }

        var current = await master.GetTenantByIdAsync(user.TenantId!.Value);
        return await ToConnectPageAsync(
            request, user, application, AgentAccessConnectTicket.Switch,
            current?.Name ?? current?.Code ?? "another organisation", hubTenant.Name, member, error);
    }

    // The original authorization request, from the switch form's hidden fields.
    private string AuthorizeUrlFromForm() =>
        Request.PathBase + Request.Path + QueryString.Create(
            OriginalParameters().Select(p => KeyValuePair.Create(p.Name, (string?)p.Value)));

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

    private async Task<IActionResult?> PassRefusalAsync(AgentAccessUser user)
    {
        if (user.ContactId is not { } contactId)
        {
            return Refuse(Errors.AccessDenied, "This user cannot connect AI apps.");
        }

        var load = await identities.LoadAsync(user.TenantId!.Value, user.UserId, contactId, HttpContext.RequestAborted);
        return load.Eligibility switch
        {
            AgentGrantEligibility.Eligible => null,
            AgentGrantEligibility.NotPermitted => Refuse(Errors.AccessDenied,
                "Your role does not allow AI app access. Ask your administrator to turn on 'Connect an AI app' for your role."),
            AgentGrantEligibility.TenantOff => Refuse(Errors.AccessDenied, "AI app access is switched off for this tenant."),
            AgentGrantEligibility.NoLongerEligible => Refuse(Errors.AccessDenied, "This user cannot connect AI apps."),
            _ => Refuse(Errors.TemporarilyUnavailable, "The user could not be checked right now; try again shortly.")
        };
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
}
