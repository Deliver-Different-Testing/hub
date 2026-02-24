using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Hub.Models.Master;
using Hub.Repositories;
using Microsoft.AspNetCore.Hosting;

namespace Hub.Services;

public class TenantService(AuthenticationRepository authenticationRepository, IWebHostEnvironment hostingEnvironment)
    : ITenantService
{
    public async Task<List<Tenant>> GetTenantsForUserAsync(int userId) =>
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
        var path = virtualPath.Replace("~/", "");
        var physicalPath = Path.Combine(hostingEnvironment.WebRootPath, path);

        // Check if the file exists
        return File.Exists(physicalPath);
    }
}