using Hub.Interfaces;

namespace Hub.Features.AgentAccess;

public readonly record struct AgentAccessTenantState(bool SwitchOn, bool ForcedOff);

// Per-tenant switch (set by the tenant admin) and the DF Admin force-off. The real store
// arrives with the Settings phase (1.7); until then the default registration reports the
// switch off, so the gate cannot open.
public interface IAgentAccessTenantState
{
    Task<AgentAccessTenantState> GetAsync(CancellationToken cancellationToken = default);
}
