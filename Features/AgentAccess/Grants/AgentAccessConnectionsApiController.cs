using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hub.Features.AgentAccess.Grants;

public sealed record AgentConnectionsResponse(
    IReadOnlyList<AgentConnectionView> Connections,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Truncated = false);

/// <param name="ActorUserId">The admin who pressed Revoke (their configurator user id), for the audit trail. Asserted by the caller.</param>
/// <param name="ActorName">Their display name.</param>
public sealed record AgentConnectionRevokeRequest(string? ActorUserId, string? ActorName);

// The admin Connected AI apps list and Revoke that configurator proxies (Authorization: Bearer <its Kubernetes service-account
// token>, validated against the cluster OIDC issuer, audience dfrnt-hub-admin, subject in AgentAccess:AdminServiceAccounts).
// Configurator decides who may call it (agent-manage-connections, DF Admin always). Everything is scoped to the tenant
// in the query string: a grant id from another tenant is simply not found.
[Route("api/agent-access/connections")]
[AllowAnonymous]
[EnableRateLimiting("api")]
public class AgentAccessConnectionsApiController(
    IAgentConnectionDirectory directory, IAgentGrantAdmin admin, IAgentAdminAudit audit, IAgentAccessAdminTokenValidator tokens) : Controller
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int tenantId,
        [FromHeader(Name = "Authorization")] string? authorization,
        CancellationToken cancellationToken)
    {
        if (await tokens.AuthenticateAsync(authorization, cancellationToken) is not { } caller) return Unauthorized();
        if (tenantId <= 0) return BadRequest();

        var list = await directory.ListAsync(tenantId, cancellationToken);
        return list is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable)
            : Ok(new AgentConnectionsResponse(list.Connections, list.Truncated));
    }

    // The same path as a user's own revoke (the authorization is revoked and agent-grant:{id} removed, so apps stop
    // honouring the pass on its next request), acting for everyone on this tenant only.
    [HttpPost("{grantId}/revoke")]
    public async Task<IActionResult> Revoke(
        string grantId,
        [FromQuery] int tenantId,
        [FromBody] AgentConnectionRevokeRequest? body,
        [FromHeader(Name = "Authorization")] string? authorization,
        CancellationToken cancellationToken)
    {
        if (await tokens.AuthenticateAsync(authorization, cancellationToken) is not { } caller) return Unauthorized();
        if (body is null || tenantId <= 0 || string.IsNullOrWhiteSpace(body.ActorUserId)) return BadRequest();

        var result = await admin.RevokeAsync(grantId, tenantId, userId: string.Empty, allUsers: true, cancellationToken);
        if (result == AgentGrantRevokeResult.Failed)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The connection could not be revoked. Try again." });
        }

        if (result is not (AgentGrantRevokeResult.Revoked or AgentGrantRevokeResult.RevokedPendingPropagation)) return NotFound();

        // Audited as soon as the authorization is revoked, even when the liveness key removal is still pending.
        // The token proves the caller is that service account; the actor is only asserted by it.
        var actorUserId = SerilogAgentAdminAudit.Clean(body.ActorUserId);
        var actorName = SerilogAgentAdminAudit.Clean(body.ActorName);
        audit.GrantRevoked(tenantId, grantId, actorUserId, actorName.Length == 0 ? actorUserId : actorName, caller);
        return result == AgentGrantRevokeResult.RevokedPendingPropagation
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = AgentAccessGrantsApiController.PendingMessage })
            : NoContent();
    }
}
