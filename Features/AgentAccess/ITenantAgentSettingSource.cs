using Hub.Interfaces;

namespace Hub.Features.AgentAccess;

/// <summary>The agent access columns of the tenant's single tblSetting row.</summary>
public sealed record TenantAgentSettingRow(string? LifecycleState, bool SwitchOn, bool ForcedOff);

// Reads TenantLifecycleState, AgentAccessEnabled and AgentAccessForcedOff from tblSetting. Returns null
// when there is no settings row. Throws when the columns do not exist yet (migration not applied), which
// the gate treats as closed.
public interface ITenantAgentSettingSource
{
    Task<TenantAgentSettingRow?> GetAsync(CancellationToken cancellationToken = default);
}
