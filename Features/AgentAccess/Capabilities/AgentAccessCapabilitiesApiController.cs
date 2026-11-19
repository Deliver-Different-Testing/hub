using Hub.Features.AgentAccess.Grants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace Hub.Features.AgentAccess.Capabilities;

/// <param name="Eligibility">Why the user can or cannot have a pass; capabilities are empty unless Eligible.</param>
/// <param name="Tools">Per tool verb: whether the verb is ticked, whether the tile and screens allow it, and why not. Empty unless Eligible.</param>
public sealed record AgentCapabilitiesResponse(
    string Eligibility, IReadOnlyList<string> Capabilities, IReadOnlyDictionary<string, AgentToolDecision>? Tools = null);

// What an AI app may do for a user right now (Access Control verbs AND the hub tiles they need). The apps ask on agent
// requests through DeliverDifferent.AgentAccess, caching for about a minute, so a role change applies within a minute
// rather than when the app pass expires. Server to server only: the app's Kubernetes service-account token (audience
// dfrnt-hub-agent-access, subject in AgentAccess:CapabilityServiceAccounts), so no secret has to match between
// deployments. X-Api-Key is still accepted from apps on an older DeliverDifferent.AgentAccess, and the scheme is
// logged so it can be removed once no tenant uses it. A token that is sent but refused is never rescued by the key.
// The answer is the same check every refresh and exchange makes.
[Route("api/agent-access/capabilities")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class AgentAccessCapabilitiesApiController(IAgentPassIdentityLoader identities, IAgentAccessCapabilitiesTokenValidator tokens)
    : Controller
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int tenantId,
        [FromQuery] string? userId,
        [FromQuery] int contactId,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromHeader(Name = "Authorization")] string? authorization,
        CancellationToken cancellationToken)
    {
        // Only a bearer token is an attempt at the token path; any other Authorization header leaves the key to decide.
        if (authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (await tokens.AuthenticateAsync(authorization, cancellationToken) is not { } caller) return Unauthorized();
            Log.Information("Agent capabilities request for tenant {TenantId} by {AuthScheme} {Caller}", tenantId, "sa-token", caller);
        }
        else if (AgentAccessApiKey.IsValid(apiKey))
        {
            Log.Information("Agent capabilities request for tenant {TenantId} by {AuthScheme}", tenantId, "api-key");
        }
        else
        {
            return Unauthorized();
        }

        if (tenantId <= 0 || string.IsNullOrWhiteSpace(userId) || contactId <= 0) return BadRequest();

        var load = await identities.LoadAsync(tenantId, userId, contactId, cancellationToken);
        if (load.Eligibility == AgentGrantEligibility.Unknown) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var capabilities = load.Eligibility == AgentGrantEligibility.Eligible
            ? AgentCapabilities.All.Where(c => load.Capabilities?.Contains(c) == true).ToList()
            : [];
        var tools = load.Eligibility == AgentGrantEligibility.Eligible
            ? load.Tools ?? new Dictionary<string, AgentToolDecision>()
            : new Dictionary<string, AgentToolDecision>();
        return Ok(new AgentCapabilitiesResponse(load.Eligibility.ToString(), capabilities, tools));
    }
}
