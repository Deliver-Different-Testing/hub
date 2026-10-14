using Hub.Interfaces;

namespace Hub.Features.AgentAccess;

public enum AgentAccessReason
{
    Enabled,
    ForcedOff,
    KeysNotPersistent,
    FeatureNotReleased,
    LifecycleUnknown,
    LifecycleNotAllowed,
    TenantSwitchOff,
    Error
}

public readonly record struct AgentAccessDecision(bool Enabled, AgentAccessReason Reason);

// Agent Access spec v4 (phase 1.1, D19): is agent access on for this tenant, right now.
// Enabled only when the agent-access Feature row is Ready or Live, the tenant switch is
// on, the tenant lifecycle is Live or Demo, DF Admin has not forced it off, and the OAuth
// keys are persistent (from SSM, with a configured issuer). Anything unknown, missing or
// failing means NOT enabled.
//
// Reads the Feature row's ReleaseStatus directly rather than through IFeatureResolver:
// the resolver renders only Live, so a Ready row would be hidden from every user.
public interface IAgentAccessGate
{
    Task<AgentAccessDecision> EvaluateAsync(CancellationToken cancellationToken = default);
}
