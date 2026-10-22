namespace Hub.ViewModels;

public sealed record TenantCountryResponse
{
    public int TenantId { get; init; }

    /// <summary>
    /// The tenant's <c>Tenant.CountryCode</c> value, a two-letter ISO country code such as "NZ".
    /// </summary>
    public required string CountryCode { get; init; }
}
