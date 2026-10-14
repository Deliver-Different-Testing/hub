using Hub.Interfaces;
using Serilog;

namespace Hub.Features.AgentAccess;

public sealed record AgentContactStatus(bool Active, int? ClientTypeId);

public interface IAgentGrantEligibilityChecker
{
    /// <summary>Re-checks, with no cookie in play, that this person can still connect AI apps in this tenant.</summary>
    Task<AgentGrantEligibility> CheckAsync(int tenantId, string userId, int contactId, CancellationToken cancellationToken = default);
}

// The claim-build re-check on refresh (phase 1.6): a deactivated contact, a changed client type or a removed user
// fails the refresh. Anything that stops the check is Unknown, never Eligible.
public sealed class AgentGrantEligibilityChecker(
    IAuthenticationRepository master,
    IDespatchRepository tenant,
    IConnectionStringManager connectionStringManager) : IAgentGrantEligibilityChecker
{
    private static readonly HashSet<int> AllowedClientTypes = [4, 5];

    public async Task<AgentGrantEligibility> CheckAsync(
        int tenantId, string userId, int contactId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!int.TryParse(userId, out var masterUserId)) return AgentGrantEligibility.NoLongerEligible;

            var user = await master.GetUserByIdAsync(masterUserId);
            if (user is null || user.IsCourier == true || !await master.IsUserAssociatedWithTenantAsync(masterUserId, tenantId))
            {
                return AgentGrantEligibility.NoLongerEligible;
            }

            var connection = await master.GetTenantConnectionStringAsync(tenantId);
            var credentials = Environment.GetEnvironmentVariable("SQLCredentials");
            if (string.IsNullOrEmpty(connection) || string.IsNullOrEmpty(credentials))
            {
                Log.Warning("Agent access refresh could not check tenant {TenantId}: no connection available", tenantId);
                return AgentGrantEligibility.Unknown;
            }

            connectionStringManager.SetConnectionString(connection + credentials);
            var contact = await tenant.FetchContactStatusAsync(contactId);

            return contact is { Active: true, ClientTypeId: { } clientType } && AllowedClientTypes.Contains(clientType)
                ? AgentGrantEligibility.Eligible
                : AgentGrantEligibility.NoLongerEligible;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Agent access refresh could not check eligibility for tenant {TenantId}", tenantId);
            return AgentGrantEligibility.Unknown;
        }
    }
}
