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
            .Select(t => new PartnerDirectoryListingResponse
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

    public async Task<PartnerDirectoryListingResponse?> GetListingAsync(int tenantId) =>
        await context.IntMgrPartnerDirectoryListings
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.IsActive)
            .Select(l => new PartnerDirectoryListingResponse
            {
                TenantId = l.TenantId,
                TenantName = l.Tenant.Name,
                BaseUrl = l.BaseUrl,
                Description = l.Description,
                Region = l.Region,
                CreatedAtUtc = l.CreatedAtUtc,
                UpdatedAtUtc = l.UpdatedAtUtc
            })
            .FirstOrDefaultAsync();

    public async Task<PartnerDirectoryListingResponse?> AddListingAsync(PartnerDirectoryListingRequest request)
    {
        var tenantName = await context.Tenants
            .Where(t => t.TenantId == request.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync();

        if (tenantName == null)
            return null;

        var exists = await context.IntMgrPartnerDirectoryListings.AnyAsync(l => l.TenantId == request.TenantId);
        if (exists)
            return null;

        var now = DateTime.UtcNow;
        var listing = new IntMgrPartnerDirectoryListing
        {
            TenantId = request.TenantId,
            BaseUrl = request.BaseUrl,
            Description = request.Description,
            Region = request.Region,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await context.IntMgrPartnerDirectoryListings.AddAsync(listing);
        await context.SaveChangesAsync();

        return new PartnerDirectoryListingResponse
        {
            TenantId = listing.TenantId,
            TenantName = tenantName,
            BaseUrl = listing.BaseUrl,
            Description = listing.Description,
            Region = listing.Region,
            CreatedAtUtc = listing.CreatedAtUtc,
            UpdatedAtUtc = listing.UpdatedAtUtc
        };
    }

    public async Task<PartnerDirectoryListingResponse?> UpdateListingAsync(int tenantId, PartnerDirectoryListingRequest request)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await context.IntMgrPartnerDirectoryListings
            .Where(l => l.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.BaseUrl, request.BaseUrl)
                .SetProperty(l => l.Description, request.Description)
                .SetProperty(l => l.Region, request.Region)
                .SetProperty(l => l.UpdatedAtUtc, now));

        if (rowsAffected == 0)
            return null;

        return await context.IntMgrPartnerDirectoryListings
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .Select(l => new PartnerDirectoryListingResponse
            {
                TenantId = l.TenantId,
                TenantName = l.Tenant.Name,
                BaseUrl = l.BaseUrl,
                Description = l.Description,
                Region = l.Region,
                CreatedAtUtc = l.CreatedAtUtc,
                UpdatedAtUtc = l.UpdatedAtUtc
            })
            .FirstOrDefaultAsync();
    }

    public async Task<bool> RemoveListingAsync(int tenantId)
    {
        var rowsAffected = await context.IntMgrPartnerDirectoryListings
            .Where(l => l.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.IsActive, false)
                .SetProperty(l => l.UpdatedAtUtc, DateTime.UtcNow));

        return rowsAffected > 0;
    }
}
