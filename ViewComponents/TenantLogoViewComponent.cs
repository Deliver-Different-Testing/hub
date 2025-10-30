using System.Threading.Tasks;
using Hub.Services;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Hub.ViewComponents;

public class TenantLogoViewComponent(ITenantLogoService tenantLogoService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string cssClass = "", string alt = "Company Logo")
    {
        var logoUrl = await tenantLogoService.GetLogoUrlAsync();
        Log.Information("ViewComponent received logo URL: {LogoUrl}", logoUrl ?? "null");

        // URL encode S3 pre-signed URLs to handle special characters in query parameters
        var encodedLogoUrl = logoUrl;
        if (logoUrl != null && !logoUrl.StartsWith("/"))
        {
            encodedLogoUrl = System.Web.HttpUtility.UrlPathEncode(logoUrl);
            Log.Information("Encoded S3 URL: {EncodedUrl}", encodedLogoUrl);
        }

        var model = new TenantLogoViewModel
        {
            LogoUrl = encodedLogoUrl ?? "/images/deliverDifferentLogo.png",
            CssClass = cssClass,
            AltText = alt,
            IsS3Logo = logoUrl != null && !logoUrl.StartsWith("/")  // Distinguishes S3 vs local
        };

        Log.Information("ViewComponent model - LogoUrl: {LogoUrl}, IsS3Logo: {IsS3Logo}, CssClass: {CssClass}",
            model.LogoUrl, model.IsS3Logo, model.CssClass);

        return View(model);
    }

    public class TenantLogoViewModel
    {
        public string LogoUrl { get; set; } = string.Empty;
        public string CssClass { get; set; } = string.Empty;
        public string AltText { get; set; } = string.Empty;
        public bool IsS3Logo { get; set; }
    }
}
