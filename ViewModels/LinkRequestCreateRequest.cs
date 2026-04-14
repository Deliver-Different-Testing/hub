namespace Hub.ViewModels;

public sealed record LinkRequestCreateRequest
{
    public int RequestingTenantId { get; init; }
    public int TargetTenantId { get; init; }
    public string? Message { get; init; }
}
