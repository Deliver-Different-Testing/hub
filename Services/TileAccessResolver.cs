using Hub.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hub.Services;

/// <inheritdoc cref="ITileAccessResolver"/>
public sealed class TileAccessResolver(DynamicDespatchDbContext context) : ITileAccessResolver
{
    // Every root of the feature tree is a hub tile, and CI enforces it:
    // navGovernance.test.ts rule 5 fails the configurator build if any non
    // `hub-tile-*` key sits at the root. That makes the prefix a contract rather
    // than a convention, so matching on it here is safe - and it avoids Hub
    // needing the ParentKey column its scaffolded Feature entity lacks.
    private const string TilePrefix = "hub-tile-";

    // ClientType 5 = DFRNTAdmin. Same signal IFeatureResolver uses for gate 1;
    // keyed on ClientType, NOT UserGroupID, so a tenant Administrator is not
    // mistaken for a DF Admin.
    private const int DfAdminClientType = 5;

    // A grant counts from View upwards. The AccessLevel ladder is vestigial
    // under the tile model - a tile is granted or it is not - but rows written
    // before the direction change may carry any level, so treat >= 1 as granted
    // rather than testing == some particular level.
    private const byte MinimumGrantedLevel = 1;

    public async Task<IReadOnlyList<TileAccess>> ResolveAsync(
        int contactId, int? clientId, ISet<string> featureEnabledKeys)
    {
        ArgumentNullException.ThrowIfNull(featureEnabledKeys);

        int? clientTypeId = clientId is > 0
            ? await context.TucClients
                .AsNoTracking()
                .Where(c => c.UcclId == clientId.Value)
                .Select(c => (int?)c.ClientTypeId)
                .FirstOrDefaultAsync()
            : null;

        // Enumerate tiles from the catalogue, not from the gate-1 result, so a
        // tile the tenant does NOT have still gets a decision. Reporting
        // "NotEnabledForTenant" is the whole point of the reason codes; silently
        // omitting the tile would leave support with nothing to look at.
        var tileKeys = await context.Features
            .AsNoTracking()
            .Where(f => f.FeatureKey.StartsWith(TilePrefix))
            .Select(f => f.FeatureKey)
            .ToListAsync();

        tileKeys.Sort(StringComparer.OrdinalIgnoreCase);

        if (clientTypeId == DfAdminClientType)
        {
            return tileKeys
                .Select(k => new TileAccess(k, true, TileAccessReason.DfAdminBypass))
                .ToList();
        }

        var roleIds = await ResolveRoleIdsAsync(contactId);
        var grants = await ResolveGrantedTilesAsync(roleIds, clientId);

        // FAIL OPEN WHEN GATE 2 IS UNCONFIGURED.
        //
        // The tenant-facing grants UI does not exist yet, so most roles have no
        // tile rows at all. Enforcing against that would take every tile from
        // every user of every tenant the moment this deploys.
        //
        // So a contact whose roles have NO tile rows in scope is not restricted
        // by gate 2 - exactly today's behaviour - and one whose roles have ANY
        // row is enforced. That makes the transition per ROLE rather than per
        // tenant: configuring one role does not strip every other role, which a
        // tenant-wide switch would.
        //
        // "CONFIGURED" MEANS ROWS EXIST, NOT THAT ANY ROW GRANTS. Testing
        // "granted.Count == 0" instead would make a role denied every tile
        // indistinguishable from a role nobody has touched, and hand it every
        // tile - the exact opposite of what whoever wrote those rows intended.
        // urgent-staging has such a role today (NP Dispatcher, every tile
        // explicitly 0), so this is a live case, not a hypothetical.
        //
        // KNOWN HOLE: a newly created role with no rows is unrestricted. That is
        // the status quo persisting until someone configures it, not a new
        // exposure - the tenant only ever sees tiles gate 1 already enabled -
        // but it is why role creation in the grants UI must write an explicit
        // row set rather than leaving the role empty.
        if (!grants.Configured)
        {
            return tileKeys
                .Select(k => featureEnabledKeys.Contains(k)
                    ? new TileAccess(k, true, TileAccessReason.NotConfigured)
                    : new TileAccess(k, false, TileAccessReason.NotEnabledForTenant))
                .ToList();
        }

        return tileKeys
            .Select(k =>
            {
                if (!featureEnabledKeys.Contains(k))
                {
                    return new TileAccess(k, false, TileAccessReason.NotEnabledForTenant);
                }

                return grants.Granted.Contains(k)
                    ? new TileAccess(k, true, TileAccessReason.Granted)
                    : new TileAccess(k, false, TileAccessReason.RoleLacksTile);
            })
            .ToList();
    }

    // Stacked roles first (tblContactContactRole), falling back to the contact's
    // single primary ContactRoleId. Mirrors the configurator's
    // RolePermissionResolver so the two apps agree on who holds what; if they
    // ever disagree, the configurator is the reference implementation.
    private async Task<List<int>> ResolveRoleIdsAsync(int contactId)
    {
        if (contactId <= 0)
        {
            return [];
        }

        var stacked = await context.TblContactContactRoles
            .AsNoTracking()
            .Where(j => j.ClientContactId == contactId)
            .Join(context.TblContactRoles.AsNoTracking().Where(r => r.IsActive),
                  j => j.ContactRoleId, r => r.ContactRoleId, (j, r) => r.ContactRoleId)
            .Distinct()
            .ToListAsync();

        if (stacked.Count > 0)
        {
            return stacked;
        }

        var single = await context.TucClientContacts
            .AsNoTracking()
            .Where(c => c.UcctId == contactId)
            .Select(c => c.ContactRoleId)
            .FirstOrDefaultAsync();

        if (single is not { } roleId || roleId <= 0)
        {
            return [];
        }

        var active = await context.TblContactRoles
            .AsNoTracking()
            .AnyAsync(r => r.ContactRoleId == roleId && r.IsActive);

        return active ? [roleId] : [];
    }

    // Union across the contact's roles, with per-client overrides beating global
    // defaults for the same (role, tile). Tiles are ROOTS of the tree, so none of
    // the configurator's cascade or walk-up-deny logic applies here - a root has
    // no ancestor to inherit from or be denied by.
    private async Task<TileGrants> ResolveGrantedTilesAsync(List<int> roleIds, int? clientId)
    {
        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (roleIds.Count == 0)
        {
            return new TileGrants(false, granted);
        }

        var rows = await context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.ContactRoleId)
                         && rp.PermissionKey.StartsWith(TilePrefix)
                         && (rp.ClientId == null || (clientId != null && rp.ClientId == clientId)))
            .Select(rp => new { rp.ContactRoleId, rp.PermissionKey, rp.ClientId, rp.AccessLevel, rp.Allowed })
            .ToListAsync();

        // Per (role, tile): a per-client override replaces the global default.
        var perRole = new Dictionary<(int Role, string Key), (bool IsOverride, byte Level)>();
        foreach (var row in rows)
        {
            var key = (row.ContactRoleId, row.PermissionKey);
            var isOverride = row.ClientId != null;
            var level = LevelOf(row.AccessLevel, row.Allowed);

            if (!perRole.TryGetValue(key, out var current) || (isOverride && !current.IsOverride))
            {
                perRole[key] = (isOverride, level);
            }
        }

        // Union across roles: the most permissive wins, so stacked roles add up.
        foreach (var ((_, tileKey), (_, level)) in perRole)
        {
            if (level >= MinimumGrantedLevel)
            {
                granted.Add(tileKey);
            }
        }

        // Configured on ROW EXISTENCE, not on whether any row grants - see the
        // note at the call site.
        return new TileGrants(rows.Count > 0, granted);
    }

    /// <param name="Configured">
    /// Whether any in-scope tile row exists for these roles. False means gate 2
    /// has not been set up for this contact and must not restrict; it does NOT
    /// mean every tile was denied.
    /// </param>
    /// <param name="Granted">Tiles resolving to at least View.</param>
    private sealed record TileGrants(bool Configured, HashSet<string> Granted);

    // Rows written before the AccessLevel backfill carry only the Allowed bit.
    private static byte LevelOf(byte? accessLevel, bool allowed)
        => accessLevel ?? (allowed ? (byte)1 : (byte)0);
}
