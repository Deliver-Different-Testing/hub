using Hub.Interfaces;
using Hub.Services;

namespace Hub.Features.AgentAccess;

// Default registrations until the real stores land. Both keep the gate closed.

/// <summary>No switch store yet (arrives in the Settings phase): switch off, not forced off.</summary>
public sealed class AgentAccessTenantStateNotConfigured : IAgentAccessTenantState
{
    public Task<AgentAccessTenantState> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgentAccessTenantState(SwitchOn: false, ForcedOff: false));
}

/// <summary>tblSetting.TenantLifecycleState does not exist yet: lifecycle unknown.</summary>
public sealed class TenantLifecycleNotConfigured : ITenantLifecycleReader
{
    public Task<TenantLifecycleState> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(TenantLifecycleState.Unknown);
}
