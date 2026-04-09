using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IPartnerDirectoryService
{
    Task<IReadOnlyList<PartnerDirectoryListingResponse>> GetActiveListingsAsync();
    Task<PartnerDirectoryListingResponse?> GetListingAsync(int tenantId);
    Task<PartnerDirectoryListingResponse?> AddListingAsync(PartnerDirectoryListingRequest request);
    Task<PartnerDirectoryListingResponse?> UpdateListingAsync(int tenantId, PartnerDirectoryListingRequest request);
    Task<bool> RemoveListingAsync(int tenantId);
}
