using Hub.Features.AgentAccess.Grants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Features.AgentAccess.Capabilities;

/// <param name="Eligibility">Why the user can or cannot have a pass; capabilities are empty unless Eligible.</param>
public sealed record AgentCapabilitiesResponse(string Eligibility, IReadOnlyList<string> Capabilities);

// What an AI app may do for a user right now (Access Control verbs AND the hub tiles they need). The apps ask on agent
// requests through DeliverDifferent.AgentAccess, caching for about a minute, so a role change applies within a minute
// rather than when the app pass expires. Server to server only, with the key the apps already send for tenant
// connections; the answer is the same check every refresh and exchange makes.
[Route("api/agent-access/capabilities")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class AgentAccessCapabilitiesApiController(IAgentPassIdentityLoader identities) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int tenantId,
        [FromQuery] string? userId,
        [FromQuery] int contactId,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (!AgentAccessApiKey.IsValid(apiKey)) return Unauthorized();
        if (tenantId <= 0 || string.IsNullOrWhiteSpace(userId) || contactId <= 0) return BadRequest();

        var load = await identities.LoadAsync(tenantId, userId, contactId, cancellationToken);
        if (load.Eligibility == AgentGrantEligibility.Unknown) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var capabilities = load.Eligibility == AgentGrantEligibility.Eligible
            ? AgentCapabilities.All.Where(c => load.Capabilities?.Contains(c) == true).ToList()
            : [];
        return Ok(new AgentCapabilitiesResponse(load.Eligibility.ToString(), capabilities));
    }
}
