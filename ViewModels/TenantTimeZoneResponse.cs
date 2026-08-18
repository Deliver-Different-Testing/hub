namespace Hub.ViewModels;

public sealed record TenantTimeZoneResponse
{
    public int TenantId { get; init; }

    /// <summary>
    /// The tenant's <c>Tenant.TimeZone</c> value, a Windows timezone id such as
    /// "New Zealand Standard Time". Callers resolve it to their own platform's timezone type.
    /// </summary>
    public required string TimeZone { get; init; }
}
