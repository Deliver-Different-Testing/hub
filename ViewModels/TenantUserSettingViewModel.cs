namespace Hub.ViewModels;

public sealed record TenantUserSettingViewModel
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Value { get; init; }
}
