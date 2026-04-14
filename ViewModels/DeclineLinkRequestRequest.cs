namespace Hub.ViewModels;

public sealed record DeclineLinkRequestRequest
{
    public string? Reason { get; init; }
}
