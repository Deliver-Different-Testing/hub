using System.Security.Claims;
using Hub.Controllers.Api;
using Hub.ViewModels;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hub.Features.AgentAccess.Oauth;

// The SPA's connect page (consent, or a switch to this Hub's organisation) reads its ticket here. The ticket only opens
// for the person and organisation it was issued to, and the page is never cached.
[Route("api/agent-access")]
[Authorize]
public class AgentAccessConnectApiController(AgentAccessConnectTickets tickets, IAntiforgery antiforgery)
    : SpaApiControllerBase
{
    [HttpGet("connect")]
    public IActionResult Connect([FromQuery] string? ticket)
    {
        Response.Headers.CacheControl = "no-store";

        if (tickets.Unprotect(ticket) is not { } sealedTicket)
        {
            return BadRequest(new ApiError("Couldn't open this connection request. It has expired. Go back to the app and connect again."));
        }

        if (!string.Equals(User.FindFirstValue("UserID"), sealedTicket.UserId, StringComparison.Ordinal)
            || User.FindFirstValue("CurrentTenantID") != sealedTicket.TenantId.ToString())
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiError("Couldn't open this connection request. It was started from another login. Go back to the app and connect again."));
        }

        // The form posts to /connect/authorize as a top-level navigation (the response redirects to the app), so it
        // carries the antiforgery request token as a form field rather than the X-XSRF-TOKEN header.
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AgentAccessConnectPage(
            sealedTicket.Kind,
            sealedTicket.ClientName,
            sealedTicket.VerifiedVendor,
            sealedTicket.ClientHost,
            sealedTicket.ResourceHost,
            sealedTicket.RedirectHost,
            sealedTicket.CurrentOrganisation,
            sealedTicket.HubOrganisation,
            sealedTicket.CanSwitch,
            sealedTicket.Error,
            sealedTicket.Parameters,
            tokens.FormFieldName,
            tokens.RequestToken!));
    }
}

public sealed record AgentAccessConnectPage(
    string Kind,
    string ClientName,
    string? VerifiedVendor,
    string? ClientHost,
    string ResourceHost,
    string? RedirectHost,
    string? CurrentOrganisation,
    string? HubOrganisation,
    bool CanSwitch,
    string? Error,
    IReadOnlyList<AgentAccessParameter> Parameters,
    string AntiforgeryFieldName,
    string AntiforgeryToken);
