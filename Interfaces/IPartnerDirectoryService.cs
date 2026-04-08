using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IPartnerDirectoryService
{
    Task<IReadOnlyList<PartnerDirectoryListingResponse>> GetActiveListingsAsync();
}
