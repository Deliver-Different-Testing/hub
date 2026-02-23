namespace Hub.ViewModels;

public class TenantBrandingResponse
{
    public int TenantId { get; set; }
    public string CompanyName { get; set; }
    public string[] AddressLines { get; set; }
    public string Country { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public string Website { get; set; }
    public string LogoUrl { get; set; }
    public string PrimaryColour { get; set; }
    public string HeaderTextColour { get; set; }
    public string AccentColour { get; set; }
    public string FooterText { get; set; }
    public string DisclaimerText { get; set; }
    public string PaperSize { get; set; }
    public string TimeZoneId { get; set; }
    public string CountryCode { get; set; }
}
