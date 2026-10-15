using Hub.Features.AgentAccess.Keys;
using Serilog;

namespace Hub.Features.AgentAccess.Gate;

/// <inheritdoc cref="IAgentAccessGate"/>
public sealed class AgentAccessGate(
    IAgentAccessFeatureReader features,
    ITenantAgentSettingSource settings,
    IAgentAccessKeyState keys) : IAgentAccessGate
{
    public const string FeatureKey = "agent-access";

    public async Task<AgentAccessDecision> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // One read of the tenant's settings row gives the force-off, the lifecycle and the switch. Cheapest and
            // most definitive first; a forced-off tenant reads nothing else.
            var row = await settings.GetAsync(cancellationToken);
            if (row?.ForcedOff == true)
            {
                return Deny(AgentAccessReason.ForcedOff);
            }

            // Never open on per-pod ephemeral keys: another pod could not verify the tokens.
            if (!keys.Persistent)
            {
                return Deny(AgentAccessReason.KeysNotPersistent);
            }

            var release = await features.GetReleaseStatusAsync(cancellationToken);
            if (!IsReleased(release))
            {
                return Deny(AgentAccessReason.FeatureNotReleased);
            }

            var lifecycleState = TenantLifecycle.Parse(row?.LifecycleState);
            if (lifecycleState == TenantLifecycleState.Unknown)
            {
                return Deny(AgentAccessReason.LifecycleUnknown);
            }

            if (lifecycleState is not (TenantLifecycleState.Live or TenantLifecycleState.Demo))
            {
                return Deny(AgentAccessReason.LifecycleNotAllowed);
            }

            return row?.SwitchOn == true
                ? new AgentAccessDecision(true, AgentAccessReason.Enabled)
                : Deny(AgentAccessReason.TenantSwitchOff);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail closed: an unreadable input never opens the gate.
            Log.Error(ex, "Agent access gate could not be evaluated; treating as disabled");
            return Deny(AgentAccessReason.Error);
        }
    }

    // Ready or Live. Beta is not a valid release state (CK_Feature_ReleaseStatus allows
    // Draft, Internal Test, Ready, Live). Case-insensitive because the column's collation is.
    private static bool IsReleased(string? status) =>
        string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Live", StringComparison.OrdinalIgnoreCase);

    private static AgentAccessDecision Deny(AgentAccessReason reason) => new(false, reason);
}

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
