using Hub.Interfaces;
using Hub.Models.Master;
using Hub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Hub.Services;

public sealed class PartnerDirectoryService(MasterContext context) : IPartnerDirectoryService
{
    public async Task<IReadOnlyList<PartnerDirectoryListingResponse>> GetActiveListingsAsync() =>
        await context.IntMgrPartnerDirectoryListings
            .AsNoTracking()
            .Where(l => l.IsActive)
            .Select(t => new PartnerDirectoryListingResponse()
            {
                TenantId = t.TenantId,
                TenantName = t.Tenant.Name,
                BaseUrl = t.BaseUrl,
                Description = t.Description,
                Region = t.Region,
                CreatedAtUtc = t.CreatedAtUtc,
                UpdatedAtUtc = t.UpdatedAtUtc
            })
            .ToListAsync();
}
