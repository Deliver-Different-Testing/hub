using Hub.Interfaces;
using Serilog;

namespace Hub.Features.AgentAccess;

public sealed record AgentPassLoad(AgentGrantEligibility Eligibility, AgentPassIdentity? Identity);

public interface IAgentPassIdentityLoader
{
    /// <summary>
    /// With no cookie in play, who is this person right now, or why can they not have a pass (phases 1.6 and 1.9).
    /// Asked once per refresh and once per exchange.
    /// </summary>
    Task<AgentPassLoad> LoadAsync(int tenantId, string userId, int contactId, CancellationToken cancellationToken = default);
}

// A deactivated contact, a changed client type, a removed user or a tenant that is switched off fails the refresh or
// the exchange. Anything that stops the check is Unknown, never Eligible.
public sealed class AgentPassIdentityLoader(
    IAuthenticationRepository master,
    IDespatchRepository tenant,
    IConnectionStringManager connectionStringManager,
    ITenantAgentSettingSource settings,
    IAgentGrantStore grants) : IAgentPassIdentityLoader
{
    private static readonly HashSet<int> AllowedClientTypes = [4, 5];

    public async Task<AgentPassLoad> LoadAsync(
        int tenantId, string userId, int contactId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!int.TryParse(userId, out var masterUserId)) return Result(AgentGrantEligibility.NoLongerEligible);

            var user = await master.GetUserByIdAsync(masterUserId);
            if (user is null || user.IsCourier == true || !await master.IsUserAssociatedWithTenantAsync(masterUserId, tenantId))
            {
                return Result(AgentGrantEligibility.NoLongerEligible);
            }

            var connection = await master.GetTenantConnectionStringAsync(tenantId);
            var credentials = Environment.GetEnvironmentVariable("SQLCredentials");
            if (string.IsNullOrEmpty(connection) || string.IsNullOrEmpty(credentials))
            {
                Log.Warning("Agent access could not check tenant {TenantId}: no connection available", tenantId);
                return Result(AgentGrantEligibility.Unknown);
            }

            connectionStringManager.SetConnectionString(connection + credentials);

            // The tenant database is the source of truth for the switch, the DF Admin force-off and the lifecycle. When it says
            // off, the Redis flag the apps check is raised (or renewed), so a missed update heals on the next refresh. Only ever
            // raised here: clearing is left to expiry so a racing refresh can never undo a switch-off.
            var row = await settings.GetAsync(cancellationToken);
            if (row is not { SwitchOn: true, ForcedOff: false } || !TenantLifecycle.AllowsAgentAccess(row.LifecycleState))
            {
                await grants.SetTenantOffAsync(tenantId, cancellationToken);
                return Result(AgentGrantEligibility.TenantOff);
            }

            var tenantRow = await master.GetTenantByIdAsync(tenantId);
            var contact = await tenant.FetchAgentContactAsync(contactId);
            if (tenantRow is null
                || contact is not { Active: true, UcctClient: { } client, UcctClientId: { } clientId, StaffId: { } staffId }
                || !AllowedClientTypes.Contains(client.ClientTypeId))
            {
                return Result(AgentGrantEligibility.NoLongerEligible);
            }

            var identity = new AgentPassIdentity(
                Email: user.Email,
                UserId: user.UserId,
                TenantId: tenantId,
                ContactId: contact.UcctId,
                ClientId: clientId,
                StaffId: staffId,
                CountryCode: tenantRow.CountryCode ?? string.Empty,
                TimeZone: tenantRow.TimeZone ?? string.Empty,
                TenantCode: tenantRow.Code ?? string.Empty,
                Internal: client.UcclInternal,
                IsNetworkPartner: user.IsNetworkPartner ?? false,
                ContactRoleId: contact.ContactRoleId,
                ClientTypeId: client.ClientTypeId,
                NpAgentId: client.NpAgentId,
                FirstName: contact.UcctFirstname ?? string.Empty,
                Surname: contact.UcctSurname ?? string.Empty,
                AccountsMode: await tenant.GetAccountsModeAsync());
            return new AgentPassLoad(AgentGrantEligibility.Eligible, identity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Agent access could not load the identity for tenant {TenantId}", tenantId);
            return Result(AgentGrantEligibility.Unknown);
        }
    }

    private static AgentPassLoad Result(AgentGrantEligibility eligibility) => new(eligibility, null);
}
