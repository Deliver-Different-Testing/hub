using Hub.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Features.AgentAccess.Grants;

public sealed record AgentGrantRevokeRequest(int TenantId, string? UserId, bool AllUsers);

public sealed record AgentConnectionNotice(string Id, string UserId, string? Email, string ClientName, DateTimeOffset? CreatedUtc);

public sealed record AgentConnectionNotifiedRequest(int TenantId, bool Sent);


// Phase 1.7: the Connected AI apps list and Revoke that Integration Manager shows. Server to server only; Integration
// Manager decides who is allowed to see everyone's grants and says so with allUsers. Hub owns the OAuth store.
[Route("api/agent-access/grants")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class AgentAccessGrantsApiController(
    IAgentGrantAdmin admin, IAgentGrantStore grants, IAuthenticationRepository master) : Controller
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

    // Integration Manager calls this the moment an admin switches agent access off or DF Admin forces it off, so apps
    // stop honouring agent requests straight away. It can only raise the flag, never clear it, so holding the key can
    // never re-open a tenant; the flag expires by itself and every refresh re-reads the tenant database.
    [HttpPut("~/api/agent-access/tenants/{tenantId:int}/off")]
    public async Task<IActionResult> SetTenantOff(
        int tenantId,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (!AgentAccessApiKey.IsValid(apiKey)) return Unauthorized();
        if (tenantId <= 0) return BadRequest();

        await grants.SetTenantOffAsync(tenantId, cancellationToken);
        return NoContent();
    }

    // The new-agent email (D20) is sent by Integration Manager, which asks Hub what is new because Hub owns the Master
    // data: who approved which app, and their address. IM marks each one once it has sent (or deliberately held) the
    // email, so nobody is told twice.
    [HttpGet("~/api/agent-access/connections/unnotified")]
    public async Task<IActionResult> Unnotified(
        [FromQuery] int tenantId, [FromHeader(Name = "X-Api-Key")] string? apiKey, CancellationToken cancellationToken)
    {
        if (!AgentAccessApiKey.IsValid(apiKey)) return Unauthorized();
        if (tenantId <= 0) return BadRequest();

        var notices = new List<AgentConnectionNotice>();
        foreach (var connection in await admin.ListUnnotifiedAsync(tenantId, cancellationToken))
        {
            var email = int.TryParse(connection.UserId, out var userId) ? (await master.GetUserByIdAsync(userId))?.Email : null;
            notices.Add(new AgentConnectionNotice(connection.Id, connection.UserId, email, connection.ClientName, connection.CreatedUtc));
        }

        return Ok(notices);
    }

    [HttpPost("~/api/agent-access/connections/{id}/notified")]
    public async Task<IActionResult> Notified(
        string id,
        [FromBody] AgentConnectionNotifiedRequest body,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (!AgentAccessApiKey.IsValid(apiKey)) return Unauthorized();
        if (body.TenantId <= 0) return BadRequest();

        return await admin.MarkNotifiedAsync(id, body.TenantId, body.Sent, cancellationToken) ? NoContent() : NotFound();
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
