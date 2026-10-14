using Hub.Interfaces;
using Serilog;

namespace Hub.Features.AgentAccess;

public sealed record AgentPassLoad(AgentGrantEligibility Eligibility, AgentPassIdentity? Identity);

public interface IAgentPassIdentityLoader
{
    /// <summary>The claim-build re-run (phase 1.9): who this person is right now, or why they cannot have a pass.</summary>
    Task<AgentPassLoad> LoadAsync(int tenantId, string userId, int contactId, CancellationToken cancellationToken = default);
}

public sealed class AgentPassIdentityLoader(
    IAgentGrantEligibilityChecker eligibility,
    IAuthenticationRepository master,
    IDespatchRepository tenant) : IAgentPassIdentityLoader
{
    public async Task<AgentPassLoad> LoadAsync(
        int tenantId, string userId, int contactId, CancellationToken cancellationToken = default)
    {
        var verdict = await eligibility.CheckAsync(tenantId, userId, contactId, cancellationToken);
        if (verdict != AgentGrantEligibility.Eligible) return new AgentPassLoad(verdict, null);

        try
        {
            var user = await master.GetUserByIdAsync(int.Parse(userId));
            var tenantRow = await master.GetTenantByIdAsync(tenantId);
            var contact = await tenant.FetchAgentContactAsync(contactId);
            if (user is null || tenantRow is null || contact?.UcctClient is null || contact.UcctClientId is null || contact.StaffId is null)
            {
                return new AgentPassLoad(AgentGrantEligibility.NoLongerEligible, null);
            }

            var identity = new AgentPassIdentity(
                Email: user.Email,
                UserId: user.UserId,
                TenantId: tenantId,
                ContactId: contact.UcctId,
                ClientId: contact.UcctClientId.Value,
                StaffId: contact.StaffId.Value,
                CountryCode: tenantRow.CountryCode ?? string.Empty,
                TimeZone: tenantRow.TimeZone ?? string.Empty,
                TenantCode: tenantRow.Code ?? string.Empty,
                Internal: contact.UcctClient.UcclInternal,
                IsNetworkPartner: user.IsNetworkPartner ?? false,
                ContactRoleId: contact.ContactRoleId,
                ClientTypeId: contact.UcctClient.ClientTypeId,
                NpAgentId: contact.UcctClient.NpAgentId,
                FirstName: contact.UcctFirstname ?? string.Empty,
                Surname: contact.UcctSurname ?? string.Empty,
                AccountsMode: await tenant.GetAccountsModeAsync());
            return new AgentPassLoad(AgentGrantEligibility.Eligible, identity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Agent access could not load the identity for tenant {TenantId}", tenantId);
            return new AgentPassLoad(AgentGrantEligibility.Unknown, null);
        }
    }
}
