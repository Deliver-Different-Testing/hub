namespace Hub.ViewModels;

public sealed record PartnerDirectoryListingResponse
{
    public int TenantId { get; init; }
    public required string TenantName { get; init; }
    public required string BaseUrl { get; init; }
    public string? Description { get; init; }
    public string? Region { get; init; }
    public bool IsActive { get; init; }
    public bool HasExistingLink { get; init; }
    public bool HasPendingRequest { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
