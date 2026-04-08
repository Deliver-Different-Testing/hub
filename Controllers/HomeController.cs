using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Hub.Models;

namespace Hub.Controllers;

[Authorize]
public class HomeController(IConnectionStringManager connectionStringManager, IDespatchRepository despatchRepository)
    : Controller
{
    public async Task<IActionResult> Index()
    {
        var cid = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ContactID")?.Value;
        var connectionString = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Connection")?.Value;
        var internalTenantUser = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Internal")?.Value;
        var userEmail = HttpContext.User.Claims.FirstOrDefault(x => x.Type == System.Security.Claims.ClaimTypes.Name)
            ?.Value;
        var tenantCode = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TenantCode")?.Value;
        if (cid == null || connectionString == null) return RedirectToAction("Login", "Account");
        Log.Debug("Found Identity for ContactID:{Cid}", cid);
        SetTenantConnectionString(connectionString);

        var contactId = int.Parse(cid);
        var internetPermissions = await despatchRepository.GetDespatchWebInternetPermissions(contactId);

        var model = new HomeViewModel
        {
            ContactId = contactId,
            GreetingString = GetGreetingString(),
            DespatchWebPermission = GetPermission(internetPermissions, 12),
            BookJobPermission = GetPermission(internetPermissions, 2),
            BulkUploadPermission = GetPermission(internetPermissions, 11),
            UserEmail = userEmail,
            TenantCode = tenantCode,
            ClientInternal = internalTenantUser,
            ShowAfterHours = await IsAfterHoursAuthorizedAsync()
        };

        return View(model);
    }

    private void SetTenantConnectionString(string dbConnection)
    {
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
            throw new InvalidOperationException("Could not find a environment variable string named 'SQLCredentials'.");
        connectionStringManager.SetConnectionString(dbConnection + credentials);
    }

    private static string GetGreetingString()
    {
        var greetings = new[]
        {
            "Hi", "Hello", "Welcome", "Greetings", "G'day", "Hey", "Good to see you,", "How are you",
            "Hope it's swell,", "How's it going", "What's good", "Howdy", "Kia ora", "Tēnā koe",
            "The world is yours,", "Hi", "Hello", "Hey", "All the best,", "Enjoy,", "Seize the day,", "Kia ora"
        };
        return greetings[Random.Shared.Next(greetings.Length)];
    }

    private static bool GetPermission(List<RVW_stpValidateInternetPermissionsResult> internetPermissions,
        int internetPermissionId) => internetPermissions.Any(i => i.InternetPermissionID == internetPermissionId);

    private async Task<bool> IsAfterHoursAuthorizedAsync()
    {
        try
        {
            var isCourierClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "IsCourier")?.Value;
            var courierIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CourierID")?.Value;

            var isCourier = !string.IsNullOrEmpty(isCourierClaim) &&
                            bool.TryParse(isCourierClaim, out var courierFlag) && courierFlag;

            if (!isCourier || string.IsNullOrEmpty(courierIdClaim) || !int.TryParse(courierIdClaim, out var courierId))
                return false;

            var isAuthorized = await despatchRepository.IsAfterHoursAuthorized(courierId);

            Log.Information("AfterHours authorization check for courier {CourierId}: {IsAuthorized}",
                courierId, isAuthorized);

            return isAuthorized;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking after-hours authorization");
            return false;
        }
    }
}