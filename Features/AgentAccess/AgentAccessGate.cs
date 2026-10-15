using Hub.Interfaces;
using Serilog;
using Hub.Services;

namespace Hub.Features.AgentAccess;

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
