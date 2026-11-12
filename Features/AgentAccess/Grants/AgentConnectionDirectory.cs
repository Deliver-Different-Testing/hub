using Hub.Features.AgentAccess.Capabilities;
using Hub.Interfaces;
using System.Text.Json.Serialization;
using Serilog;

namespace Hub.Features.AgentAccess.Grants;

/// <param name="GrantId">The OpenIddict authorization id; what revoke takes.</param>
/// <param name="Client">The AI app's display name (Claude, ChatGPT, ...).</param>
/// <param name="Capabilities">What the connection can do now, from the same loader every refresh uses; empty when the holder is not eligible.</param>
/// <param name="Eligibility">Set only when the holder would be refused at the next refresh (NoLongerEligible, TenantOff, NotPermitted, Unknown).</param>
public sealed record AgentConnectionView(
    string GrantId,
    string UserId,
    int? ContactId,
    string UserName,
    string? Email,
    IReadOnlyList<string> Roles,
    string Client,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastUsedAt,
    IReadOnlyList<string> Capabilities,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Eligibility = null);

/// <param name="Truncated">True when the tenant has more live grants than the list cap, so only the newest are shown.</param>
public sealed record AgentConnectionList(IReadOnlyList<AgentConnectionView> Connections, bool Truncated);

public interface IAgentConnectionDirectory
{
    /// <summary>The newest live grants on the tenant (capped), with who holds them and what they can do now. Null when the tenant cannot be reached.</summary>
    Task<AgentConnectionList?> ListAsync(int tenantId, CancellationToken cancellationToken = default);
}

public sealed class AgentConnectionDirectory(
    IAgentGrantAdmin admin,
    IAuthenticationRepository master,
    IDespatchRepository tenant,
    IConnectionStringManager connectionStringManager,
    IAgentCapabilityResolver capabilities,
    IAgentPassIdentityLoader identityLoader) : IAgentConnectionDirectory
{
    internal const int MaxGrants = 500;

    private sealed record Holder(string? Email, string? Name, IReadOnlyList<string> Roles, IReadOnlyList<string> Capabilities, string? Eligibility);

    public async Task<AgentConnectionList?> ListAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        // One more than the cap, newest first, so a capped list is known to be capped.
        var all = await admin.ListNewestAsync(tenantId, MaxGrants + 1, cancellationToken);
        if (all.Count == 0) return new AgentConnectionList([], false);

        var truncated = all.Count > MaxGrants;
        var grants = truncated ? all.Take(MaxGrants).ToList() : all;

        var connection = await master.GetTenantConnectionStringAsync(tenantId);
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials");
        var tenantRow = await master.GetTenantByIdAsync(tenantId);
        if (string.IsNullOrEmpty(connection) || string.IsNullOrEmpty(credentials) || tenantRow is null)
        {
            Log.Warning("Agent access could not list connections for tenant {TenantId}: no connection available", tenantId);
            return null;
        }

        connectionStringManager.SetConnectionString(connection + credentials);

        // One lookup per user and contact for the whole request, however many grants they hold.
        var holders = new Dictionary<(string UserId, int? ContactId), Holder>();
        var views = new List<AgentConnectionView>();
        foreach (var grant in grants)
        {
            try
            {
                var key = (grant.UserId, grant.ContactId);
                if (!holders.TryGetValue(key, out var holder))
                {
                    holder = await LoadHolderAsync(grant, tenantId, cancellationToken);
                    holders[key] = holder;
                }

                views.Add(new AgentConnectionView(
                    grant.Id, grant.UserId, grant.ContactId, string.IsNullOrWhiteSpace(holder.Name) ? "Unknown user" : holder.Name, holder.Email,
                    holder.Roles, grant.ClientName, grant.CreatedUtc, grant.LastUsedUtc, holder.Capabilities, Eligibility: holder.Eligibility));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable grant must not hide the rest (or the ability to revoke it).
                Log.Warning(ex, "Agent access could not read grant {GrantId} on tenant {TenantId}", grant.Id, tenantId);
                views.Add(new AgentConnectionView(
                    grant.Id, grant.UserId, grant.ContactId, "Unknown user", null, [], grant.ClientName, grant.CreatedUtc, grant.LastUsedUtc, [],
                    "could not be read"));
            }
        }

        return new AgentConnectionList(views, truncated);
    }

    private async Task<Holder> LoadHolderAsync(AgentGrantSummary grant, int tenantId, CancellationToken cancellationToken)
    {
        var user = int.TryParse(grant.UserId, out var userId) ? await master.GetUserByIdAsync(userId) : null;
        var contact = grant.ContactId is { } contactId ? await tenant.FetchAgentContactAsync(contactId) : null;

        var contactName = contact is null ? null : $"{contact.UcctFirstname} {contact.UcctSurname}".Trim();
        var name = string.IsNullOrWhiteSpace(contactName) ? user?.Email : contactName;
        var roles = contact is null ? [] : await capabilities.RoleNamesAsync(contact.UcctId, cancellationToken);

        // The same eligibility checks and capabilities the next refresh applies, so the list never shows what the
        // grant could no longer do.
        var load = grant.ContactId is { } id
            ? await identityLoader.LoadAsync(tenantId, grant.UserId, id, cancellationToken)
            : new AgentPassLoad(AgentGrantEligibility.NoLongerEligible, null);
        var eligible = load.Eligibility == AgentGrantEligibility.Eligible;
        IReadOnlySet<string> granted = eligible ? load.Capabilities ?? new HashSet<string>() : new HashSet<string>();

        return new Holder(
            user?.Email, name, roles, AgentCapabilities.All.Where(granted.Contains).ToList(),
            eligible ? null : load.Eligibility.ToString());
    }
}
