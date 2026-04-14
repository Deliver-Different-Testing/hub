using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IPartnerDirectoryService
{
    Task<IReadOnlyList<PartnerDirectoryListingResponse>> GetActiveListingsAsync(int? viewingTenantId = null);
    Task<PartnerDirectoryListingResponse?> GetListingAsync(int tenantId);
    Task<PartnerDirectoryListingResponse?> AddListingAsync(PartnerDirectoryListingRequest request);
    Task<PartnerDirectoryListingResponse?> UpdateListingAsync(int tenantId, PartnerDirectoryListingRequest request);
    Task<bool> RemoveListingAsync(int tenantId);
    Task<bool> ActivateListingAsync(int tenantId);
    Task<LinkRequestResponse?> CreateLinkRequestAsync(LinkRequestCreateRequest request);
    Task<IReadOnlyList<LinkRequestResponse>> GetLinkRequestsAsync(int tenantId);
    Task<LinkRequestResponse?> AcceptLinkRequestAsync(int requestId);
    Task<LinkRequestResponse?> DeclineLinkRequestAsync(int requestId, string? reason);
}
