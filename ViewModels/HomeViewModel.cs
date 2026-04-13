namespace Hub.ViewModels;

public sealed record HomeViewModel
{
    public int ContactId { get; init; }

    public bool DespatchWebPermission { get; init; }
    public bool BookJobPermission { get; init; }
    public bool BulkUploadPermission { get; init; }
    public string? UserEmail { get; init; }
    public string? TenantCode { get; init; }
    public string? ClientInternal { get; init; }
    public bool ShowAfterHours { get; init; }
}
