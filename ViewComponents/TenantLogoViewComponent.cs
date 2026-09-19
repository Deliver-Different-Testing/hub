using Hub.Interfaces;
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
        if (logoUrl != null && !logoUrl.StartsWith('/'))
        {
            encodedLogoUrl = System.Web.HttpUtility.UrlPathEncode(logoUrl);
            Log.Information("Encoded S3 URL: {EncodedUrl}", encodedLogoUrl);
        }

        var isS3Logo = logoUrl != null && !logoUrl.StartsWith('/');  // Distinguishes S3 vs local
        var resolvedLogoUrl = encodedLogoUrl ?? "/images/DFRNT_HorizLogo_RGB.png";

        // Client-side <img onerror> fallback for when the S3 pre-signed URL fails
        // to load (expired/403/access). Urgent keeps its own brand art; everyone
        // else lands on the universal DFRNT wordmark. Read the tenant claim
        // null-safely — the component is constructed without a ViewComponentContext
        // in unit tests, so `User` would throw. Matches _Layout.cshtml's claim read.
        var tenantCode = ViewComponentContext?.ViewContext?.HttpContext?
            .User?.FindFirst("TenantCode")?.Value ?? string.Empty;
        var fallbackLogoUrl = string.Equals(tenantCode, "urgent", StringComparison.OrdinalIgnoreCase)
            ? "/images/urgentCouriersNewLogo.png"
            : "/images/DFRNT_HorizLogo_RGB.png";

        var model = new TenantLogoViewModel
        {
            LogoUrl = resolvedLogoUrl,
            FallbackLogoUrl = fallbackLogoUrl,
            // Local (our own) logos ship a dark-theme variant with a light wordmark
            // ("<name>_dark.<ext>") so we can asset-swap on dark surfaces instead of
            // plating the light logo behind a box. S3 tenant logos are arbitrary
            // third-party art we can't recolour, so they get no dark variant.
            DarkLogoUrl = isS3Logo ? null : DeriveDarkVariant(resolvedLogoUrl),
            CssClass = cssClass,
            AltText = alt,
            IsS3Logo = isS3Logo
        };

        Log.Information("ViewComponent model - LogoUrl: {LogoUrl}, IsS3Logo: {IsS3Logo}, CssClass: {CssClass}",
            model.LogoUrl, model.IsS3Logo, model.CssClass);

        return View(model);
    }

    // "/images/DFRNT_HorizLogo_RGB.png" -> "/images/DFRNT_HorizLogo_RGB_dark.png"
    private static string DeriveDarkVariant(string url)
    {
        var dot = url.LastIndexOf('.');
        return dot > 0 ? string.Concat(url.AsSpan(0, dot), "_dark", url.AsSpan(dot)) : url + "_dark";
    }

    public class TenantLogoViewModel
    {
        public string LogoUrl { get; init; } = string.Empty;
        public string FallbackLogoUrl { get; init; } = "/images/DFRNT_HorizLogo_RGB.png";
        public string? DarkLogoUrl { get; init; }
        public string CssClass { get; init; } = string.Empty;
        public string AltText { get; init; } = string.Empty;
        public bool IsS3Logo { get; init; }
    }
}
