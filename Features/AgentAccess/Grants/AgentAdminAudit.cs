using Serilog;

namespace Hub.Features.AgentAccess.Grants;

/// <summary>The audit trail for cross-user admin actions on AI app connections.</summary>
public interface IAgentAdminAudit
{
    /// <summary>An admin ended someone else's AI app connection. Written only after the grant was revoked.</summary>
    /// <param name="caller">The system that holds the API key and made the call (authenticated).</param>
    void GrantRevoked(int tenantId, string grantId, string actorUserId, string actorName, string caller);
}

// Hub has no audit table, so the trail is a structured Serilog event: Audit=true, Action, the authenticated Caller and
// the admin as actor, which the log search (CloudWatch) can filter on. The actor is asserted by the caller, not proven.
public sealed class SerilogAgentAdminAudit : IAgentAdminAudit
{
    public const string ConfiguratorCaller = "configurator";
    private const int MaxActorLength = 100;

    public void GrantRevoked(int tenantId, string grantId, string actorUserId, string actorName, string caller) =>
        Log.ForContext("Audit", true)
            .ForContext("Action", "AgentGrantRevokedByAdmin")
            .ForContext("Caller", caller)
            .Information(
                "Agent access audit: {Actor} (user {ActorUserId}, asserted by {Caller}) revoked AI app grant {GrantId} on tenant {TenantId}",
                Clean(actorName), Clean(actorUserId), Clean(caller), Clean(grantId), tenantId);

    /// <summary>Trims, drops control characters (no forged log lines) and caps the length of caller-supplied text.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > MaxActorLength ? cleaned[..MaxActorLength] : cleaned;
    }
}
