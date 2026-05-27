using Hub.Interfaces;
using Hub.Models;
using Microsoft.EntityFrameworkCore;

namespace Hub.Services;

/// <inheritdoc cref="IFeatureResolver"/>
public sealed class FeatureResolver(DynamicDespatchDbContext context) : IFeatureResolver
{
    // NULL ClientTypeId → 2 (Customer) per §1.4 of the Permissions plan.
    private const int NullClientTypeFallback = 2;

    public async Task<HashSet<string>> ResolveVisibleFeaturesAsync(int? clientTypeId)
    {
        var effectiveId = clientTypeId ?? NullClientTypeFallback;

        var keys = await context.ClientTypeFeatures
            .AsNoTracking()
            .Where(ctf => ctf.ClientTypeId == effectiveId && ctf.Visible)
            .Select(ctf => ctf.FeatureKey)
            .ToListAsync();

        return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<HashSet<string>> ResolveForClientAsync(int? clientId, bool isDfAdmin)
    {
        if (isDfAdmin)
        {
            // Union of every visible feature key across all ClientTypes.
            // Defensive cover for any DF admin still on a legacy Internal (1)
            // or NULL ClientType row that hasn't been reparented to
            // DFRNTAdmin (5) yet — they see everything visible anywhere.
            var allKeys = await context.ClientTypeFeatures
                .AsNoTracking()
                .Where(ctf => ctf.Visible)
                .Select(ctf => ctf.FeatureKey)
                .Distinct()
                .ToListAsync();
            return new HashSet<string>(allKeys, StringComparer.OrdinalIgnoreCase);
        }

        // Non-admin: look up tucClient.ClientTypeId, then resolve.
        int? clientTypeId = null;
        if (clientId is > 0)
        {
            clientTypeId = await context.TucClients
                .AsNoTracking()
                .Where(c => c.UcclId == clientId.Value)
                .Select(c => c.ClientTypeId)
                .FirstOrDefaultAsync();
        }

        return await ResolveVisibleFeaturesAsync(clientTypeId);
    }
}
