namespace Hub.ViewModels;

public sealed record TenantConnectionStringResponse
{
    public int TenantId { get; init; }
    public required string ConnectionString { get; init; }
}
