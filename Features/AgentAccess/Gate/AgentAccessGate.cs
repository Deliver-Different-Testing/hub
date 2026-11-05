using Hub.Features.AgentAccess.Keys;
using Serilog;

namespace Hub.Features.AgentAccess.Gate;

/// <inheritdoc cref="IAgentAccessGate"/>
public sealed class AgentAccessGate(
    ITenantAgentSettingSource settings,
    IAgentAccessKeyState keys) : IAgentAccessGate
{
    public async Task<AgentAccessDecision> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // One read of the tenant's settings row gives the force-off and the switch. Cheapest and
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

    private static AgentAccessDecision Deny(AgentAccessReason reason) => new(false, reason);
}

public enum AgentAccessReason
{
    Enabled,
    ForcedOff,
    KeysNotPersistent,
    TenantSwitchOff,
    Error
}

public readonly record struct AgentAccessDecision(bool Enabled, AgentAccessReason Reason);

// Is agent access on for this tenant, right now. Enabled only when the tenant switch
// (tblSetting.AgentAccessEnabled) is on, DF Admin has not forced it off, and the OAuth keys are
// persistent (from SSM, with a configured issuer). Anything unknown, missing or failing means
// NOT enabled. The Feature matrix and the tenant lifecycle play no part.
public interface IAgentAccessGate
{
    Task<AgentAccessDecision> EvaluateAsync(CancellationToken cancellationToken = default);
}
