namespace Hub.ViewModels;

public sealed record TenantViewModel
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
}
