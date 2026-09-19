namespace Hub.ViewModels;

public sealed record LinkRequestResponse
{
    public int Id { get; init; }
    public int RequestingTenantId { get; init; }
    public required string RequestingTenantName { get; init; }
    public int TargetTenantId { get; init; }
    public required string TargetTenantName { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
    public string? DeclineReason { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
