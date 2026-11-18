using System.Security.Claims;
using Hub.Interfaces;
using Hub.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Hub.Features.ApiKeys;

/// <summary>What the signed-in person may do on the Settings page. One answer for the menu, the page and the endpoint.</summary>
public sealed record ApiKeyAccess(bool CanSeeSettings, bool CanGenerateApiKey)
{
    public static ApiKeyAccess None { get; } = new(false, false);
    public static ApiKeyAccess Full { get; } = new(true, true);
}

public interface IApiKeyAccessResolver
{
    Task<ApiKeyAccess> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

// Hub API keys: only a tenant's administrator may create one (Steve, 13 Nov 2026, spec
// GARRY-HUB-API-KEY-TENANT-ADMIN-ONLY). A key is a long-lived JWT that the api never expires, so a shared staff login
// must not be able to turn into one.
//
//   Customer (ClientTypeId 2, or no client type)   Settings + key (self-serve keys are deliberate)
//   Tenant user (4) whose role holds manage-api-keys Settings + key
//   Tenant user (4) without it                      neither
//   DF Admin (5)                                    Settings + key
//   Network Partner (3), courier, anything else     neither
//
// The verb fails closed like the agent-* verbs: only an explicit row on manage-api-keys itself at level 1 or more
// counts (global or the client's override; an override beats the global row for that role; duplicates at the same
// scope take the lowest level; union across stacked roles). It never cascades on from an ancestor. Any error is "no".
// Not UserGroupID and not the Internal flag: the internal flag carries no permissions (8 Oct rule).
public sealed class ApiKeyAccessResolver(
    DynamicDespatchDbContext context,
    IConnectionStringManager connectionStringManager) : IApiKeyAccessResolver
{
    public const string ManageApiKeys = "manage-api-keys";

    private const int Customer = 2;
    private const int Tenant = 4;
    private const int DfAdmin = 5;

    public async Task<ApiKeyAccess> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (string.Equals(user.FindFirst("IsCourier")?.Value, "True", StringComparison.OrdinalIgnoreCase)) return ApiKeyAccess.None;

        var clientTypeClaim = user.FindFirst("ClientTypeId")?.Value;
        if (string.IsNullOrEmpty(clientTypeClaim)) return ApiKeyAccess.Full;
        if (!int.TryParse(clientTypeClaim, out var clientType)) return ApiKeyAccess.None;

        switch (clientType)
        {
            case Customer:
            case DfAdmin:
                return ApiKeyAccess.Full;
            case Tenant:
                return await HoldsVerbAsync(user, cancellationToken) ? ApiKeyAccess.Full : ApiKeyAccess.None;
            default:
                return ApiKeyAccess.None;
        }
    }

    private async Task<bool> HoldsVerbAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        try
        {
            if (!int.TryParse(user.FindFirst("ContactID")?.Value, out var contactId) || contactId <= 0) return false;
            int? clientId = int.TryParse(user.FindFirst("ClientID")?.Value, out var c) && c > 0 ? c : null;

            // A standalone request: the scoped connection is empty until something sets it from the cookie.
            var connection = user.FindFirst("Connection")?.Value;
            var credentials = Environment.GetEnvironmentVariable("SQLCredentials");
            if (!string.IsNullOrEmpty(connection) && !string.IsNullOrEmpty(credentials))
            {
                connectionStringManager.SetConnectionString(connection + credentials);
            }

            var roleIds = await RoleIdsAsync(contactId, cancellationToken);
            if (roleIds.Count == 0) return false;

            var rows = await context.RolePermissions.AsNoTracking()
                .Where(rp => roleIds.Contains(rp.ContactRoleId)
                             && rp.PermissionKey == ManageApiKeys
                             && (rp.ClientId == null || (clientId != null && rp.ClientId == clientId)))
                .Select(rp => new { rp.ContactRoleId, IsOverride = rp.ClientId != null, rp.AccessLevel, rp.Allowed })
                .ToListAsync(cancellationToken);

            return rows
                .GroupBy(r => r.ContactRoleId)
                .Any(role =>
                {
                    var scope = role.Any(r => r.IsOverride) ? role.Where(r => r.IsOverride) : role;
                    return scope.Min(r => r.AccessLevel ?? (r.Allowed ? (byte)1 : (byte)0)) >= 1;
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Could not check {Verb} for contact {ContactId}; treating as not allowed",
                ManageApiKeys, user.FindFirst("ContactID")?.Value);
            return false;
        }
    }

    // Stacked roles first, else the contact's single primary role; inactive roles count for nothing. Same rule as
    // AgentCapabilityResolver.
    private async Task<List<int>> RoleIdsAsync(int contactId, CancellationToken cancellationToken)
    {
        var stacked = await context.TblContactContactRoles.AsNoTracking()
            .Where(j => j.ClientContactId == contactId)
            .Join(context.TblContactRoles.AsNoTracking().Where(r => r.IsActive),
                j => j.ContactRoleId, r => r.ContactRoleId, (j, r) => r.ContactRoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (stacked.Count > 0) return stacked;

        var single = await context.TucClientContacts.AsNoTracking()
            .Where(c => c.UcctId == contactId)
            .Select(c => c.ContactRoleId)
            .FirstOrDefaultAsync(cancellationToken);
        if (single is not { } roleId || roleId <= 0) return [];

        return await context.TblContactRoles.AsNoTracking().AnyAsync(r => r.ContactRoleId == roleId && r.IsActive, cancellationToken)
            ? [roleId]
            : [];
    }
}
