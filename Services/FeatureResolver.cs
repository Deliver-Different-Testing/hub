using Hub.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hub.Services;

/// <inheritdoc cref="IFeatureResolver"/>
public sealed class FeatureResolver(DynamicDespatchDbContext context) : IFeatureResolver
{
    // NULL ClientTypeId → 2 (Customer) per §1.4 of the Permissions plan.
    private const int NullClientTypeFallback = 2;

    // ClientType 4 = Tenant, 5 = DFRNTAdmin.
    private const int TenantClientType = 4;
    private const int DfAdminClientType = 5;

    // A feature reaches a tenant only when ClientVisible AND ReleaseStatus ==
    // Live, so unfinished work cannot surface just because its parent tile is
    // enabled. Must stay in step with the configurator ClientTypeFeatureResolver:
    // the two apps read the same catalogue and a disagreement shows up as a hub
    // tile that leads to an empty sidebar.
    private const string LiveReleaseStatus = "Live";

    // DF ADMIN CEILING (Steve decision note 2026-09-01, step 3). Effective
    // visibility is Visible AND Grantable: Visible is what the tenant granted
    // in Tile Access, Grantable is whether DF Admin allowed them to. Enforced
    // here and not only in the configurator setter, so that LOWERING the
    // ceiling revokes immediately rather than merely blocking the next click.
    //
    // Applied to the DF Admin union below as well: a row the ceiling has closed
    // grants nobody anything, so it must not carry a tile into the union
    // either. Must stay in step with the configurator ClientTypeFeatureResolver
    // - the two apps read the same catalogue, and a disagreement here shows up
    // as a hub tile that leads to an empty sidebar.

    public async Task<HashSet<string>> ResolveVisibleFeaturesAsync(int? clientTypeId)
    {
        var effectiveId = clientTypeId ?? NullClientTypeFallback;

        var keys = await context.ClientTypeFeatures
            .AsNoTracking()
            .Where(ctf => ctf.ClientTypeId == effectiveId && ctf.Visible && ctf.Grantable)
            .Join(context.Features.AsNoTracking()
                     .Where(f => f.ClientVisible && f.ReleaseStatus == LiveReleaseStatus),
                  ctf => ctf.FeatureKey, f => f.FeatureKey, (ctf, f) => ctf)
            .Select(ctf => ctf.FeatureKey)
            .ToListAsync();

        return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<HashSet<string>> ResolveForClientAsync(int? clientId, bool isInternal = false)
    {
        // Look up the user's ClientType from their client.
        int? clientTypeId = null;
        if (clientId is > 0)
        {
            clientTypeId = await context.TucClients
                .AsNoTracking()
                .Where(c => c.UcclId == clientId.Value)
                .Select(c => c.ClientTypeId)
                .FirstOrDefaultAsync();
        }

        // INTERNAL STAFF RESOLVE AS TENANT, NOT AS CUSTOMER.
        //
        // The tenant's own people sit on Customer (ClientType 2) clients
        // carrying ucclInternal = 1 - section 3 of the unified permissions spec
        // warns about exactly this, and StaffLaneSignal in the configurator
        // keys off the same flag. Taking their ClientType literally would
        // resolve them against the Customer row set, which on urgent-staging is
        // three tiles: they would lose Dispatch, Accounts, Admin Manager and
        // the rest the moment the internal branch of Index.cshtml starts
        // honouring this set.
        //
        // Mapping them to Tenant is both correct and safe. ucclInternal = 1
        // means "this client is us", and ClientType 4 already carries a visible
        // row for every hub tile, so this preserves exactly what internal staff
        // see today rather than granting anything new.
        //
        // NOT applied to a DF Admin: ClientType 5 has its own bypass below and
        // must keep it.
        if (isInternal && clientTypeId != DfAdminClientType)
        {
            clientTypeId = TenantClientType;
        }

        // DF Admin bypass — ClientType=5 (DFRNTAdmin) sees the union of every
        // visible feature key across all ClientTypes. Keyed on ClientType so a
        // tenant Administrator (UserGroupID=1 on a ClientTypeId=4 client) is NOT
        // treated as a DF admin — was a caller-supplied isDfAdmin bool keyed on
        // UserGroupID==1; switched to match the configurator's signal.
        if (clientTypeId != DfAdminClientType)
        {
            return await ResolveVisibleFeaturesAsync(clientTypeId);
        }

        var allKeys = await context.ClientTypeFeatures
            .AsNoTracking()
            .Where(ctf => ctf.Visible && ctf.Grantable)
            .Select(ctf => ctf.FeatureKey)
            .Distinct()
            .ToListAsync();
        
        return new HashSet<string>(allKeys, StringComparer.OrdinalIgnoreCase);
    }
}
