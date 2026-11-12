using Hub.Interfaces;
using Hub.Models;
using Microsoft.EntityFrameworkCore;

namespace Hub.Features.AgentAccess.Capabilities;

/// <summary>The Access Control verbs (dbo.Feature, under agent-access) that say what an AI app may do for a user.</summary>
public static class AgentCapabilities
{
    public const string ParentKey = "agent-access";

    public const string Connect = "agent-connect";
    public const string ExportJobs = "agent-export-jobs";
    public const string ExportPricing = "agent-export-pricing";
    public const string SeeCosts = "agent-see-costs";

    public static readonly IReadOnlyList<string> All = [Connect, ExportJobs, ExportPricing, SeeCosts];

    // An export may never show more than the user can open in the app it reads from.
    internal static readonly IReadOnlyDictionary<string, string> ExportTiles = new Dictionary<string, string>
    {
        [ExportJobs] = "hub-tile-despatchweb",
        [ExportPricing] = "hub-tile-configurator"
    };
}

/// <param name="ContactId">tucClientContact.ucctID.</param>
/// <param name="ClientId">The contact's client, for per-client overrides and the DF Admin check.</param>
/// <param name="Internal">tucClient.ucclInternal: tenant staff on a Customer client resolve as the tenant for gate 1.</param>
/// <param name="CountryCode">The tenant's market, for gate 1's country scope.</param>
public sealed record AgentCapabilitySubject(int ContactId, int? ClientId, bool Internal, string? CountryCode);

public interface IAgentCapabilityResolver
{
    /// <summary>The capabilities this contact's AI apps hold now. Empty means none, including no agent-connect.</summary>
    Task<IReadOnlySet<string>> ResolveAsync(AgentCapabilitySubject subject, CancellationToken cancellationToken = default);
}

// Every agent capability needs agent-connect. Each verb is resolved like the configurator's RolePermissionResolver
// (per-client override beats the global row, union across stacked roles, an explicit None on an ancestor denies, else
// the nearest explicit level up the tree). Verbs are default off because every role carries an explicit None row
// (migration + RolesController.DefaultOffVerbKeys); without one a verb would cascade on from its tile. An export also
// needs the hub tile of the app it reads, decided by Hub's own gates 1 and 2, so it never shows more than the launcher.
public sealed class AgentCapabilityResolver(
    DynamicDespatchDbContext context,
    IFeatureResolver featureResolver,
    ITileAccessResolver tileAccessResolver) : IAgentCapabilityResolver
{
    private const int DfAdminClientType = 5;

    public async Task<IReadOnlySet<string>> ResolveAsync(AgentCapabilitySubject subject, CancellationToken cancellationToken = default)
    {
        var clientTypeId = subject.ClientId is > 0
            ? await context.TucClients.AsNoTracking()
                .Where(c => c.UcclId == subject.ClientId.Value)
                .Select(c => (int?)c.ClientTypeId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (clientTypeId == DfAdminClientType)
        {
            return AgentCapabilities.All.ToHashSet(StringComparer.Ordinal);
        }

        var roleIds = await RoleIdsAsync(subject.ContactId, cancellationToken);
        var verbs = await GrantedVerbsAsync(roleIds, subject.ClientId, cancellationToken);
        if (!verbs.Contains(AgentCapabilities.Connect))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var tiles = await GrantedTilesAsync(subject);
        return verbs
            .Where(v => !AgentCapabilities.ExportTiles.TryGetValue(v, out var tile) || tiles.Contains(tile))
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<HashSet<string>> GrantedTilesAsync(AgentCapabilitySubject subject)
    {
        var enabled = await featureResolver.ResolveForClientAsync(subject.ClientId, subject.Internal, subject.CountryCode);
        var decisions = await tileAccessResolver.ResolveAsync(subject.ContactId, subject.ClientId, enabled);
        return decisions.Where(d => d.Granted).Select(d => d.TileKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Stacked roles first, else the contact's single primary role; inactive roles count for nothing.
    private async Task<List<int>> RoleIdsAsync(int contactId, CancellationToken cancellationToken)
    {
        if (contactId <= 0) return [];

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

    private async Task<HashSet<string>> GrantedVerbsAsync(List<int> roleIds, int? clientId, CancellationToken cancellationToken)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        if (roleIds.Count == 0) return granted;

        var parents = await context.Features.AsNoTracking()
            .Select(f => new { f.FeatureKey, f.ParentKey })
            .ToDictionaryAsync(f => f.FeatureKey, f => f.ParentKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

        // Only the verbs and their ancestors decide anything here.
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var verb in AgentCapabilities.All)
        {
            for (var k = verb; k is not null && keys.Add(k); k = parents.GetValueOrDefault(k)) { }
        }

        var rows = await context.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.ContactRoleId)
                         && keys.Contains(rp.PermissionKey)
                         && (rp.ClientId == null || (clientId != null && rp.ClientId == clientId)))
            .Select(rp => new { rp.ContactRoleId, rp.PermissionKey, rp.ClientId, rp.AccessLevel, rp.Allowed })
            .ToListAsync(cancellationToken);

        var perRole = new Dictionary<(int Role, string Key), (bool IsOverride, byte Level)>();
        foreach (var row in rows)
        {
            var key = (row.ContactRoleId, row.PermissionKey.ToLowerInvariant());
            var isOverride = row.ClientId != null;
            if (!perRole.TryGetValue(key, out var current) || (isOverride && !current.IsOverride))
            {
                perRole[key] = (isOverride, row.AccessLevel ?? (row.Allowed ? (byte)1 : (byte)0));
            }
        }

        var levels = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var ((_, key), (_, level)) in perRole)
        {
            if (!levels.TryGetValue(key, out var current) || level > current) levels[key] = level;
        }

        foreach (var verb in AgentCapabilities.All)
        {
            if (Level(verb, parents, levels) >= 1) granted.Add(verb);
        }

        return granted;
    }

    // An explicit None on any ancestor denies; else the node's own explicit level; else the nearest explicit ancestor.
    private static byte Level(string key, Dictionary<string, string> parents, Dictionary<string, byte> levels)
    {
        for (var a = parents.GetValueOrDefault(key); a is not null; a = parents.GetValueOrDefault(a))
        {
            if (levels.TryGetValue(a, out var l) && l == 0) return 0;
        }

        for (var k = key; k is not null; k = parents.GetValueOrDefault(k))
        {
            if (levels.TryGetValue(k, out var l)) return l;
        }

        return 0;
    }
}
