using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Features.AgentAccess;

public sealed record AgentGrantRevokeRequest(int TenantId, string? UserId, bool AllUsers);

// Phase 1.7: the Connected AI apps list and Revoke that Integration Manager shows. Server to server only; Integration
// Manager decides who is allowed to see everyone's grants and says so with allUsers. Hub owns the OAuth store.
[Route("api/agent-access/grants")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class AgentAccessGrantsApiController(IAgentGrantAdmin admin) : Controller
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int tenantId,
        [FromQuery] string? userId,
        [FromQuery] bool allUsers,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (!AgentAccessApiKey.IsValid(apiKey)) return Unauthorized();
        if (tenantId <= 0 || string.IsNullOrWhiteSpace(userId)) return BadRequest();

        return Ok(await admin.ListAsync(tenantId, userId, allUsers, cancellationToken));
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> Revoke(
        string id,
        [FromBody] AgentGrantRevokeRequest body,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (!AgentAccessApiKey.IsValid(apiKey)) return Unauthorized();
        if (body.TenantId <= 0 || string.IsNullOrWhiteSpace(body.UserId)) return BadRequest();

        var result = await admin.RevokeAsync(id, body.TenantId, body.UserId, body.AllUsers, cancellationToken);
        return result == AgentGrantRevokeResult.Revoked ? NoContent() : NotFound();
    }
}
