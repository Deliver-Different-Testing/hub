using Hub.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hub.Services;

/// <inheritdoc cref="IFeatureResolver"/>
public sealed class FeatureResolver(DynamicDespatchDbContext context) : IFeatureResolver
{
    // NULL ClientTypeId → 2 (Customer) per §1.4 of the Permissions plan.
    private const int NullClientTypeFallback = 2;

    // A feature reaches a tenant only when ClientVisible AND ReleaseStatus ==
    // Live, so unfinished work cannot surface just because its parent tile is
    // enabled. Must stay in step with the configurator ClientTypeFeatureResolver:
    // the two apps read the same catalogue and a disagreement shows up as a hub
    // tile that leads to an empty sidebar.
    private const string LiveReleaseStatus = "Live";

    public async Task<HashSet<string>> ResolveVisibleFeaturesAsync(int? clientTypeId)
    {
        var effectiveId = clientTypeId ?? NullClientTypeFallback;

        var keys = await context.ClientTypeFeatures
            .AsNoTracking()
            .Where(ctf => ctf.ClientTypeId == effectiveId && ctf.Visible)
            .Join(context.Features.AsNoTracking()
                     .Where(f => f.ClientVisible && f.ReleaseStatus == LiveReleaseStatus),
                  ctf => ctf.FeatureKey, f => f.FeatureKey, (ctf, f) => ctf)
            .Select(ctf => ctf.FeatureKey)
            .ToListAsync();

        return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<HashSet<string>> ResolveForClientAsync(int? clientId)
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

        // DF Admin bypass — ClientType=5 (DFRNTAdmin) sees the union of every
        // visible feature key across all ClientTypes. Keyed on ClientType so a
        // tenant Administrator (UserGroupID=1 on a ClientTypeId=4 client) is NOT
        // treated as a DF admin — was a caller-supplied isDfAdmin bool keyed on
        // UserGroupID==1; switched to match the configurator's signal.
        if (clientTypeId != 5)
        {
            return await ResolveVisibleFeaturesAsync(clientTypeId);
        }

        var allKeys = await context.ClientTypeFeatures
            .AsNoTracking()
            .Where(ctf => ctf.Visible)
            .Select(ctf => ctf.FeatureKey)
            .Distinct()
            .ToListAsync();
        
        return new HashSet<string>(allKeys, StringComparer.OrdinalIgnoreCase);
    }
}
