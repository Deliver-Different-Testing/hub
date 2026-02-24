using System.Linq;
using System.Threading.Tasks;
using Hub.Models.Master;
using Hub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Hub.Services;

public class TenantBrandingConfigService(
    MasterContext context,
    ITenantLogoService tenantLogoService) : ITenantBrandingConfigService
{
    public async Task<TenantBrandingResponse> GetReportConfigAsync(int tenantId)
    {
        var result = await context.TenantBrandings
            .Where(tb => tb.TenantId == tenantId)
            .Join(
                context.Tenants,
                tb => tb.TenantId,
                t => t.TenantId,
                (tb, t) => new { Branding = tb, Tenant = t })
            .FirstOrDefaultAsync();

        if (result == null)
            return null;

        var branding = result.Branding;
        var tenant = result.Tenant;

        var addressLines = new[] { branding.AddressLine1, branding.AddressLine2, branding.AddressLine3 }
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToArray();

        var logoUrl = await tenantLogoService.GetLogoUrlAsync();

        return new TenantBrandingResponse
        {
            TenantId = branding.TenantId,
            CompanyName = branding.CompanyName,
            AddressLines = addressLines,
            Country = branding.Country,
            Phone = branding.Phone,
            Email = branding.Email,
            Website = branding.Website,
            LogoUrl = logoUrl,
            PrimaryColour = branding.PrimaryColour,
            HeaderTextColour = branding.HeaderTextColour,
            AccentColour = branding.AccentColour,
            FooterText = branding.FooterText,
            DisclaimerText = branding.DisclaimerText,
            PaperSize = branding.PaperSize,
            TimeZoneId = tenant.TimeZone,
            CountryCode = tenant.CountryCode
        };
    }
}