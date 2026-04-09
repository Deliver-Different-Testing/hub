namespace Hub.ViewModels;

public sealed record PartnerDirectoryListingRequest
{
    public int TenantId { get; init; }
    public required string BaseUrl { get; init; }
    public string? Description { get; init; }
    public string? Region { get; init; }
}
