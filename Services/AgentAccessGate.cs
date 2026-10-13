using Hub.Interfaces;
using Serilog;

namespace Hub.Services;

/// <inheritdoc cref="IAgentAccessGate"/>
public sealed class AgentAccessGate(
    IAgentAccessFeatureReader features,
    IAgentAccessTenantState tenantState,
    ITenantLifecycleReader lifecycle) : IAgentAccessGate
{
    public const string FeatureKey = "agent-access";

    public async Task<AgentAccessDecision> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Cheapest and most definitive first; a forced-off tenant reads nothing else.
            var state = await tenantState.GetAsync(cancellationToken);
            if (state.ForcedOff)
            {
                return Deny(AgentAccessReason.ForcedOff);
            }

            var release = await features.GetReleaseStatusAsync(cancellationToken);
            if (!IsReleased(release))
            {
                return Deny(AgentAccessReason.FeatureNotReleased);
            }

            var lifecycleState = await lifecycle.GetAsync(cancellationToken);
            if (lifecycleState == TenantLifecycleState.Unknown)
            {
                return Deny(AgentAccessReason.LifecycleUnknown);
            }

            if (lifecycleState is not (TenantLifecycleState.Live or TenantLifecycleState.Demo))
            {
                return Deny(AgentAccessReason.LifecycleNotAllowed);
            }

            return state.SwitchOn
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
