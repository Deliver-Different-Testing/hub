using Hub.Interfaces;

namespace Hub.Features.AgentAccess;

// Tenant lifecycle states from the environment and release policy, section 4.
public enum TenantLifecycleState
{
    Unknown,
    Provisioned,
    Configuring,
    PreLiveUat,
    ReadyForGoLive,
    Live,
    Suspended,
    Demo
}

// The policy keeps the state in tblSetting.TenantLifecycleState, which does not exist in
// the schema yet. The default registration reports Unknown, so the gate stays closed until
// a real reader replaces it.
public interface ITenantLifecycleReader
{
    Task<TenantLifecycleState> GetAsync(CancellationToken cancellationToken = default);
}
