using Hub.Interfaces;
using Hub.Models.Master;
using Hub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Hub.Services;

public sealed class PartnerDirectoryService(MasterContext context) : IPartnerDirectoryService
{
    public async Task<IReadOnlyList<PartnerDirectoryListingResponse>> GetActiveListingsAsync(
        int? viewingTenantId = null)
    {
        var listings = await context.IntMgrPartnerDirectoryListings
            .Where(l => l.IsActive)
            .Select(t => new PartnerDirectoryListingResponse
            {
                TenantId = t.TenantId,
                TenantName = t.Tenant.Name,
                BaseUrl = t.BaseUrl,
                Description = t.Description,
                Region = t.Region,
                IsActive = t.IsActive,
                CreatedAtUtc = t.CreatedAtUtc,
                UpdatedAtUtc = t.UpdatedAtUtc
            })
            .ToListAsync();

        if (viewingTenantId is not { } tenantId)
        {
            return listings;
        }

        var linkRequests = await context.IntMgrPartnerDirectoryLinkRequests
            .Where(r => (r.RequestingTenantId == tenantId || r.TargetTenantId == tenantId)
                        && (r.Status == "Accepted" || r.Status == "Pending"))
            .Select(r => new { r.RequestingTenantId, r.TargetTenantId, r.Status })
            .ToListAsync();

        var acceptedPartners = linkRequests
            .Where(r => r.Status == "Accepted")
            .Select(r => r.RequestingTenantId == tenantId ? r.TargetTenantId : r.RequestingTenantId)
            .ToHashSet();

        var pendingPartners = linkRequests
            .Where(r => r.Status == "Pending")
            .Select(r => r.RequestingTenantId == tenantId ? r.TargetTenantId : r.RequestingTenantId)
            .ToHashSet();

        return listings.Select(l => l with
        {
            HasExistingLink = acceptedPartners.Contains(l.TenantId),
            HasPendingRequest = pendingPartners.Contains(l.TenantId)
        }).ToList();
    }

    public async Task<PartnerDirectoryListingResponse?> GetListingAsync(int tenantId) =>
        await context.IntMgrPartnerDirectoryListings
            .Where(l => l.TenantId == tenantId)
            .Select(l => new PartnerDirectoryListingResponse
            {
                TenantId = l.TenantId,
                TenantName = l.Tenant.Name,
                BaseUrl = l.BaseUrl,
                Description = l.Description,
                Region = l.Region,
                IsActive = l.IsActive,
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
        {
            return null;
        }

        var exists = await context.IntMgrPartnerDirectoryListings.AnyAsync(l => l.TenantId == request.TenantId);
        if (exists)
        {
            return null;
        }

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
            IsActive = listing.IsActive,
            CreatedAtUtc = listing.CreatedAtUtc,
            UpdatedAtUtc = listing.UpdatedAtUtc
        };
    }

    public async Task<PartnerDirectoryListingResponse?> UpdateListingAsync(int tenantId,
        PartnerDirectoryListingRequest request)
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
        {
            return null;
        }

        return await context.IntMgrPartnerDirectoryListings
            .Where(l => l.TenantId == tenantId)
            .Select(l => new PartnerDirectoryListingResponse
            {
                TenantId = l.TenantId,
                TenantName = l.Tenant.Name,
                BaseUrl = l.BaseUrl,
                Description = l.Description,
                Region = l.Region,
                IsActive = l.IsActive,
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

    public async Task<bool> ActivateListingAsync(int tenantId)
    {
        var rowsAffected = await context.IntMgrPartnerDirectoryListings
            .Where(l => l.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.IsActive, true)
                .SetProperty(l => l.UpdatedAtUtc, DateTime.UtcNow));

        return rowsAffected > 0;
    }

    public async Task<LinkRequestResponse?> CreateLinkRequestAsync(LinkRequestCreateRequest request)
    {
        var requestingTenant = await context.Tenants
            .Where(t => t.TenantId == request.RequestingTenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync();

        var targetTenant = await context.Tenants
            .Where(t => t.TenantId == request.TargetTenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync();

        if (requestingTenant is null || targetTenant is null)
        {
            return null;
        }

        var pendingExists = await context.IntMgrPartnerDirectoryLinkRequests
            .AnyAsync(r => r.RequestingTenantId == request.RequestingTenantId
                           && r.TargetTenantId == request.TargetTenantId
                           && r.Status == "Pending");

        if (pendingExists)
        {
            throw new InvalidOperationException("A pending link request already exists between these tenants.");
        }

        var now = DateTime.UtcNow;
        var entity = new IntMgrPartnerDirectoryLinkRequest
        {
            RequestingTenantId = request.RequestingTenantId,
            TargetTenantId = request.TargetTenantId,
            Status = "Pending",
            Message = request.Message,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await context.IntMgrPartnerDirectoryLinkRequests.AddAsync(entity);
        await context.SaveChangesAsync();

        return new LinkRequestResponse
        {
            Id = entity.Id,
            RequestingTenantId = entity.RequestingTenantId,
            RequestingTenantName = requestingTenant,
            TargetTenantId = entity.TargetTenantId,
            TargetTenantName = targetTenant,
            Status = entity.Status,
            Message = entity.Message,
            DeclineReason = entity.DeclineReason,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }

    public async Task<IReadOnlyList<LinkRequestResponse>> GetLinkRequestsAsync(int tenantId) =>
        await context.IntMgrPartnerDirectoryLinkRequests
            .Where(r => r.RequestingTenantId == tenantId || r.TargetTenantId == tenantId)
            .Select(r => new LinkRequestResponse
            {
                Id = r.Id,
                RequestingTenantId = r.RequestingTenantId,
                RequestingTenantName = r.RequestingTenant.Name,
                TargetTenantId = r.TargetTenantId,
                TargetTenantName = r.TargetTenant.Name,
                Status = r.Status,
                Message = r.Message,
                DeclineReason = r.DeclineReason,
                CreatedAtUtc = r.CreatedAtUtc,
                UpdatedAtUtc = r.UpdatedAtUtc
            })
            .ToListAsync();

    public async Task<LinkRequestResponse?> AcceptLinkRequestAsync(int requestId)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await context.IntMgrPartnerDirectoryLinkRequests
            .Where(r => r.Id == requestId && r.Status == "Pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "Accepted")
                .SetProperty(r => r.UpdatedAtUtc, now));

        if (rowsAffected is 0)
        {
            return null;
        }

        return await context.IntMgrPartnerDirectoryLinkRequests
            .Where(r => r.Id == requestId)
            .Select(r => new LinkRequestResponse
            {
                Id = r.Id,
                RequestingTenantId = r.RequestingTenantId,
                RequestingTenantName = r.RequestingTenant.Name,
                TargetTenantId = r.TargetTenantId,
                TargetTenantName = r.TargetTenant.Name,
                Status = r.Status,
                Message = r.Message,
                DeclineReason = r.DeclineReason,
                CreatedAtUtc = r.CreatedAtUtc,
                UpdatedAtUtc = r.UpdatedAtUtc
            })
            .FirstOrDefaultAsync();
    }

    public async Task<LinkRequestResponse?> DeclineLinkRequestAsync(int requestId, string? reason)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await context.IntMgrPartnerDirectoryLinkRequests
            .Where(r => r.Id == requestId && r.Status == "Pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "Declined")
                .SetProperty(r => r.DeclineReason, reason)
                .SetProperty(r => r.UpdatedAtUtc, now));

        if (rowsAffected is 0)
        {
            return null;
        }

        return await context.IntMgrPartnerDirectoryLinkRequests
            .Where(r => r.Id == requestId)
            .Select(r => new LinkRequestResponse
            {
                Id = r.Id,
                RequestingTenantId = r.RequestingTenantId,
                RequestingTenantName = r.RequestingTenant.Name,
                TargetTenantId = r.TargetTenantId,
                TargetTenantName = r.TargetTenant.Name,
                Status = r.Status,
                Message = r.Message,
                DeclineReason = r.DeclineReason,
                CreatedAtUtc = r.CreatedAtUtc,
                UpdatedAtUtc = r.UpdatedAtUtc
            })
            .FirstOrDefaultAsync();
    }

    public async Task<LinkRequestResponse?> ClearLinkRequestAsync(int requestId, string? reason)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await context.IntMgrPartnerDirectoryLinkRequests
            .Where(r => r.Id == requestId && (r.Status == "Pending" || r.Status == "Accepted"))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "Declined")
                .SetProperty(r => r.DeclineReason, reason)
                .SetProperty(r => r.UpdatedAtUtc, now));

        if (rowsAffected is 0)
        {
            return null;
        }

        return await context.IntMgrPartnerDirectoryLinkRequests
            .Where(r => r.Id == requestId)
            .Select(r => new LinkRequestResponse
            {
                Id = r.Id,
                RequestingTenantId = r.RequestingTenantId,
                RequestingTenantName = r.RequestingTenant.Name,
                TargetTenantId = r.TargetTenantId,
                TargetTenantName = r.TargetTenant.Name,
                Status = r.Status,
                Message = r.Message,
                DeclineReason = r.DeclineReason,
                CreatedAtUtc = r.CreatedAtUtc,
                UpdatedAtUtc = r.UpdatedAtUtc
            })
            .FirstOrDefaultAsync();
    }
}