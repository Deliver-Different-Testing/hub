using Hub.Interfaces;
using Hub.Models.Master;

namespace Hub.Services;

public sealed class TenantService(IAuthenticationRepository authenticationRepository, IWebHostEnvironment hostingEnvironment)
    : ITenantService
{
    public async Task<IReadOnlyList<Tenant>> GetTenantsForUserAsync(int userId) =>
        await authenticationRepository.GetTenantsByUserIdAsync(userId);

    public string GetTenantLogoPath(string tenantCode)
    {
        const string defaultLogo = "~/images/DFRNT_HorizLogo_RGB.png"; // Default logo
        if (string.IsNullOrEmpty(tenantCode))
            return defaultLogo;


        var tenantLogoPath = $"~/images/{tenantCode}Logo.png";


        return LogoFileExists(tenantLogoPath)
            ? tenantLogoPath
            :
            // Fall back to the default logo if tenant-specific logo doesn't exist
            defaultLogo;
    }

    private bool LogoFileExists(string virtualPath)
    {
        // Convert virtual path (~/...) to physical path
        var path = virtualPath.Replace("~/", string.Empty);
        var physicalPath = Path.Combine(hostingEnvironment.WebRootPath, path);

        // Check if the file exists
        return File.Exists(physicalPath);
    }
}