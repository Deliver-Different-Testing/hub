namespace Hub.ViewModels;

public sealed record TenantBrandingResponse
{
    public int TenantId { get; init; }
    public required string CompanyName { get; init; }
    public required string[] AddressLines { get; init; }
    public required string Country { get; init; }
    public required string Phone { get; init; }
    public required string Email { get; init; }
    public required string Website { get; init; }
    public required string LogoUrl { get; init; }
    public required string PrimaryColour { get; init; }
    public required string HeaderTextColour { get; init; }
    public required string AccentColour { get; init; }
    public required string FooterText { get; init; }
    public required string DisclaimerText { get; init; }
    public required string PaperSize { get; init; }
    public required string TimeZoneId { get; init; }
    public required string CountryCode { get; init; }
}
